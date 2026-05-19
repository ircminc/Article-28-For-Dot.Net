using System.Text.Json;
using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

/// <summary>
/// Aggregations over parsed claims + APG results — port of the Python
/// <c>analytics_engine.py</c>. Per-user isolation is applied at the
/// query root via <see cref="OwnedQueryExtensions.OwnedBy"/> so analysts
/// only see analytics on their own claims.
///
/// Metric definitions (auditable):
///   compression_pct      = (correct_apg - actual_paid) / correct_apg × 100
///                          (positive = underpaid)
///   paid_as_pct_of_billed = actual_paid / billed × 100
///   denial_rate          = count(claim_status == '4') / count(claims) × 100
///   underpayment_total   = sum(variance) where variance > 0
/// </summary>
public interface IAnalyticsService
{
    Task<AnalyticsViewModel> ComputeAsync(
        AnalyticsFilters filters, CancellationToken ct = default);
}

public class AnalyticsService(
    ApplicationDbContext db,
    ICurrentUserContext currentUser,
    ILogger<AnalyticsService> log) : IAnalyticsService
{
    public async Task<AnalyticsViewModel> ComputeAsync(
        AnalyticsFilters filters, CancellationToken ct = default)
    {
        var vm = new AnalyticsViewModel { Filters = filters };

        // ------------------------------------------------------------
        // Build the base claim query: per-user isolation + filter bar.
        // Every metric below derives from this single root.
        // ------------------------------------------------------------
        var claimsQuery = ApplyClaimFilters(db.ParsedClaims.OwnedBy(currentUser), filters);
        var ownerFilter = currentUser.EffectiveOwnerFilter;

        // Likewise for ApgResults, which doesn't have its own OwnerUserId —
        // it's filtered through the parent ParsedClaim.
        IQueryable<ApgResultRecord> apgQuery = db.ApgResults
            .Where(a => a.Claim != null);
        if (ownerFilter is not null)
            apgQuery = apgQuery.Where(a => a.Claim!.OwnerUserId == ownerFilter);
        apgQuery = ApplyClaimFiltersOnApg(apgQuery, filters);

        // Likewise for Adjustments (CAS rows) for the Denials panel.
        IQueryable<ClaimAdjustment> adjQuery = db.ClaimAdjustments
            .Where(a => a.Claim != null);
        if (ownerFilter is not null)
            adjQuery = adjQuery.Where(a => a.Claim!.OwnerUserId == ownerFilter);
        adjQuery = ApplyClaimFiltersOnAdjustment(adjQuery, filters);

        // ------------------------------------------------------------
        // 1. Top-line summary KPIs
        // ------------------------------------------------------------
        vm.TotalClaims         = await claimsQuery.CountAsync(ct);
        vm.ClaimsWithApgResult = await apgQuery.CountAsync(ct);
        vm.TotalBilled         = await claimsQuery.SumAsync(c => (decimal?)c.BilledAmount, ct) ?? 0m;
        vm.TotalPaid           = await claimsQuery.SumAsync(c => (decimal?)c.PaidAmount,   ct) ?? 0m;
        vm.TotalCorrectApg     = await apgQuery.SumAsync(a => (decimal?)a.CorrectApgPayment, ct) ?? 0m;
        vm.TotalVariance       = await apgQuery.SumAsync(a => (decimal?)a.Variance, ct) ?? 0m;
        vm.UnderpaymentTotal   = await apgQuery
            .Where(a => a.Variance > 0)
            .SumAsync(a => (decimal?)a.Variance, ct) ?? 0m;
        vm.AvgCompressionPct   = vm.ClaimsWithApgResult == 0 ? 0m
            : (await apgQuery.AverageAsync(a => (decimal?)a.CompressionPct, ct) ?? 0m);

        // ------------------------------------------------------------
        // 2. Status counts (single round trip via conditional sums)
        // ------------------------------------------------------------
        if (vm.ClaimsWithApgResult > 0)
        {
            var counts = await apgQuery
                .GroupBy(a => 1)
                .Select(g => new
                {
                    Underpaid = g.Sum(a => a.Underpaid ? 1 : 0),
                    Overpaid  = g.Sum(a => a.Overpaid  ? 1 : 0),
                    Match     = g.Sum(a => !a.Underpaid && !a.Overpaid ? 1 : 0),
                })
                .FirstOrDefaultAsync(ct);
            vm.Underpaid = counts?.Underpaid ?? 0;
            vm.Overpaid  = counts?.Overpaid  ?? 0;
            vm.Match     = counts?.Match     ?? 0;
        }
        vm.Unpriced = vm.TotalClaims - vm.ClaimsWithApgResult;

        // ------------------------------------------------------------
        // 3. Denial rate (claim_status = '4' on 835 remits)
        // ------------------------------------------------------------
        if (vm.TotalClaims > 0)
        {
            var denied = await claimsQuery.CountAsync(c => c.ClaimStatus == "4", ct);
            vm.DenialRatePct = decimal.Round((decimal)denied / vm.TotalClaims * 100m, 2);
        }

        // ------------------------------------------------------------
        // 4. File-type breakdown
        // ------------------------------------------------------------
        vm.ByFileType = await claimsQuery
            .GroupBy(c => c.FileType)
            .Select(g => new FileTypeStat
            {
                FileType = g.Key,
                Count = g.Count(),
                TotalBilled = g.Sum(c => c.BilledAmount),
                TotalPaid = g.Sum(c => c.PaidAmount),
                TotalCorrectApg = g.Sum(c => c.ApgResult == null ? 0 : c.ApgResult.CorrectApgPayment),
                TotalVariance = g.Sum(c => c.ApgResult == null ? 0 : c.ApgResult.Variance),
            })
            .OrderBy(s => s.FileType)
            .ToListAsync(ct);

        // ------------------------------------------------------------
        // 5. Top 10 underpaid + overpaid claims
        // ------------------------------------------------------------
        vm.TopUnderpaid = await claimsQuery
            .Include(c => c.ApgResult)
            .Where(c => c.ApgResult != null && c.ApgResult.Underpaid)
            .OrderByDescending(c => c.ApgResult!.Variance)
            .Take(10)
            .Select(c => new TopVarianceRow
            {
                Id = c.Id,
                ClaimId = c.ClaimId,
                FileType = c.FileType,
                PatientName = c.PatientName,
                DateOfService = c.DateOfService,
                CorrectApg = c.ApgResult!.CorrectApgPayment,
                Paid = c.ApgResult.ActualPaid,
                Variance = c.ApgResult.Variance,
            })
            .ToListAsync(ct);

        vm.TopOverpaid = await claimsQuery
            .Include(c => c.ApgResult)
            .Where(c => c.ApgResult != null && c.ApgResult.Overpaid)
            .OrderBy(c => c.ApgResult!.Variance)
            .Take(10)
            .Select(c => new TopVarianceRow
            {
                Id = c.Id,
                ClaimId = c.ClaimId,
                FileType = c.FileType,
                PatientName = c.PatientName,
                DateOfService = c.DateOfService,
                CorrectApg = c.ApgResult!.CorrectApgPayment,
                Paid = c.ApgResult.ActualPaid,
                Variance = c.ApgResult.Variance,
            })
            .ToListAsync(ct);

        // ------------------------------------------------------------
        // 6. Trends (time series)
        // ------------------------------------------------------------
        var trendRaw = await claimsQuery
            .Where(c => c.DateOfService != null)
            .GroupBy(c => new { c.DateOfService!.Value.Year, c.DateOfService!.Value.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Claims = g.Count(),
                Billed = g.Sum(c => c.BilledAmount),
                Paid = g.Sum(c => c.PaidAmount),
                Variance = g.Sum(c => c.ApgResult == null ? 0m : c.ApgResult.Variance),
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToListAsync(ct);

        if (filters.TrendPeriod == "quarterly")
        {
            // Roll monthly buckets up to YYYY-Qn in memory.
            var quarterly = trendRaw
                .GroupBy(x => new { x.Year, Q = (x.Month - 1) / 3 + 1 })
                .Select(g => new TrendPoint
                {
                    Period = $"{g.Key.Year}-Q{g.Key.Q}",
                    Claims = g.Sum(x => x.Claims),
                    Billed = g.Sum(x => x.Billed),
                    Paid = g.Sum(x => x.Paid),
                    Variance = g.Sum(x => x.Variance),
                })
                .OrderBy(t => t.Period)
                .ToList();
            vm.Trends = quarterly;
        }
        else
        {
            vm.Trends = trendRaw
                .Select(x => new TrendPoint
                {
                    Period = $"{x.Year:D4}-{x.Month:D2}",
                    Claims = x.Claims,
                    Billed = x.Billed,
                    Paid = x.Paid,
                    Variance = x.Variance,
                })
                .ToList();
        }

        // ------------------------------------------------------------
        // 7. Denials by CARC (group_code, reason_code)
        // ------------------------------------------------------------
        var denialRows = await adjQuery
            .GroupBy(a => new { a.GroupCode, a.ReasonCode })
            .Select(g => new
            {
                g.Key.GroupCode,
                g.Key.ReasonCode,
                Count = g.Count(),
                Amount = g.Sum(a => a.Amount),
            })
            .OrderByDescending(x => x.Amount)
            .Take(50)   // generous so the panel can show top 20+ comfortably
            .ToListAsync(ct);

        var totalAdjAmount = denialRows.Sum(r => r.Amount);
        vm.TotalAdjustmentsAmount = totalAdjAmount;
        vm.Denials = denialRows.Select(r => new DenialRow
        {
            GroupCode = r.GroupCode ?? "",
            ReasonCode = r.ReasonCode ?? "",
            Count = r.Count,
            TotalAmount = r.Amount,
            PctOfAdjustments = totalAdjAmount == 0 ? 0m
                : decimal.Round(r.Amount / totalAdjAmount * 100m, 2),
        }).ToList();

        // ------------------------------------------------------------
        // 8. Top underpaid procedures + 9. Compression breakdown
        //    Both are CompressionRow lists with different bucketing.
        // ------------------------------------------------------------
        var compRows = await ComputeCompressionAsync(claimsQuery, apgQuery, filters.GroupBy, ct);
        vm.Compression = compRows.OrderByDescending(r => r.Variance).Take(20).ToList();

        // Top underpaid procedures = compression by procedure, variance > 0, top 10.
        var byProcedure = filters.GroupBy == "procedure"
            ? compRows
            : await ComputeCompressionAsync(claimsQuery, apgQuery, "procedure", ct);
        vm.TopUnderpaidProcedures = byProcedure
            .Where(r => r.Variance > 0)
            .OrderByDescending(r => r.Variance)
            .Take(10)
            .ToList();

        // ------------------------------------------------------------
        // 10. Payer scorecard
        // ------------------------------------------------------------
        var payerMain = await claimsQuery
            .GroupBy(c => c.PayerName)
            .Select(g => new
            {
                PayerName = g.Key,
                Claims = g.Count(),
                Billed = g.Sum(c => c.BilledAmount),
                Paid = g.Sum(c => c.PaidAmount),
                Denied = g.Sum(c => c.ClaimStatus == "4" ? 1 : 0),
            })
            .ToListAsync(ct);

        var payerApg = (await apgQuery
            .GroupBy(a => a.Claim!.PayerName)
            .Select(g => new
            {
                PayerName = g.Key,
                ApgClaims = g.Count(),
                Variance = g.Sum(a => a.Variance),
                AvgComp = g.Average(a => (decimal?)a.CompressionPct) ?? 0m,
                Underpaid = g.Sum(a => a.Variance > 0 ? a.Variance : 0m),
            })
            .ToListAsync(ct))
            .ToDictionary(x => x.PayerName ?? "", x => x);

        vm.PayerScorecard = payerMain
            .Select(p =>
            {
                payerApg.TryGetValue(p.PayerName ?? "", out var apg);
                return new PayerScorecardRow
                {
                    PayerName = p.PayerName ?? "(unknown)",
                    Claims = p.Claims,
                    Billed = p.Billed,
                    Paid = p.Paid,
                    PaidPctOfBilled = p.Billed == 0 ? 0m
                        : decimal.Round(p.Paid / p.Billed * 100m, 2),
                    Denied = p.Denied,
                    DenialRatePct = p.Claims == 0 ? 0m
                        : decimal.Round((decimal)p.Denied / p.Claims * 100m, 2),
                    ApgClaims = apg?.ApgClaims ?? 0,
                    ApgVarianceTotal = apg?.Variance ?? 0m,
                    ApgUnderpaymentTotal = apg?.Underpaid ?? 0m,
                    ApgAvgCompressionPct = apg?.AvgComp ?? 0m,
                };
            })
            .OrderByDescending(p => p.Claims)
            .ToList();

        // ------------------------------------------------------------
        // 11. Filter dropdown sources (distinct payers / NPIs in the user's claims)
        // ------------------------------------------------------------
        vm.AllPayers = await db.ParsedClaims
            .OwnedBy(currentUser)
            .Where(c => c.PayerName != null && c.PayerName != "")
            .Select(c => c.PayerName!)
            .Distinct()
            .OrderBy(p => p)
            .Take(200)
            .ToListAsync(ct);

        vm.AllProviderNpis = await db.ParsedClaims
            .OwnedBy(currentUser)
            .Where(c => c.ProviderNpi != null && c.ProviderNpi != "")
            .Select(c => c.ProviderNpi!)
            .Distinct()
            .OrderBy(p => p)
            .Take(200)
            .ToListAsync(ct);

        return vm;
    }

    // -------------------------------------------------------------------
    // Compression breakdown — bucketed by EAPG / procedure / peer-group / region / year
    // -------------------------------------------------------------------
    private async Task<List<CompressionRow>> ComputeCompressionAsync(
        IQueryable<ParsedClaim> claimsQuery,
        IQueryable<ApgResultRecord> apgQuery,
        string groupBy,
        CancellationToken ct)
    {
        switch (groupBy)
        {
            case "peer_group":
            case "region":
            {
                var col = groupBy;
                var rows = await apgQuery
                    .GroupBy(a => col == "peer_group" ? a.PeerGroup : a.Region)
                    .Select(g => new CompressionRow
                    {
                        Bucket = g.Key ?? "(unknown)",
                        Count = g.Count(),
                        Expected = g.Sum(a => a.CorrectApgPayment),
                        Paid = g.Sum(a => a.ActualPaid),
                        Variance = g.Sum(a => a.Variance),
                        AvgCompressionPct = g.Average(a => (decimal?)a.CompressionPct) ?? 0m,
                    })
                    .ToListAsync(ct);
                return rows.OrderByDescending(r => r.Variance).ToList();
            }

            case "date_year":
            {
                var rows = await claimsQuery
                    .Where(c => c.DateOfService != null && c.ApgResult != null)
                    .GroupBy(c => c.DateOfService!.Value.Year)
                    .Select(g => new CompressionRow
                    {
                        Bucket = g.Key.ToString(),
                        Count = g.Count(),
                        Expected = g.Sum(c => c.ApgResult!.CorrectApgPayment),
                        Paid = g.Sum(c => c.ApgResult!.ActualPaid),
                        Variance = g.Sum(c => c.ApgResult!.Variance),
                        AvgCompressionPct = g.Average(c => (decimal?)c.ApgResult!.CompressionPct) ?? 0m,
                    })
                    .OrderBy(r => r.Bucket)
                    .ToListAsync(ct);
                return rows;
            }

            case "eapg":
            case "procedure":
            default:
            {
                // Both EAPG and procedure bucketing live inside the
                // ApgResult.LineDetailsJson column (an array of APGLineResult).
                // The only way to aggregate is to materialize and unpack
                // in memory. This is bounded by ApgResult row count, which
                // is small relative to reference data — fine for the analytics
                // workloads we care about (1k–100k claims).
                var resultBlobs = await apgQuery
                    .Where(a => a.LineDetailsJson != null && a.LineDetailsJson != "")
                    .Select(a => a.LineDetailsJson)
                    .ToListAsync(ct);

                var buckets = new Dictionary<string, CompressionAccumulator>();
                foreach (var json in resultBlobs)
                {
                    if (string.IsNullOrEmpty(json)) continue;
                    List<APGLineResult>? lines;
                    try
                    {
                        lines = JsonSerializer.Deserialize<List<APGLineResult>>(json);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Skipping malformed line_details JSON in analytics aggregation");
                        continue;
                    }
                    if (lines is null) continue;

                    foreach (var l in lines)
                    {
                        string key;
                        if (groupBy == "eapg")
                        {
                            key = l.Eapg.HasValue ? l.Eapg.Value.ToString() : "(unknown)";
                            if (!string.IsNullOrEmpty(l.EapgDesc)) key = $"{key} — {l.EapgDesc}";
                        }
                        else
                        {
                            key = string.IsNullOrEmpty(l.ProcedureCode) ? "(unknown)" : l.ProcedureCode;
                        }

                        if (!buckets.TryGetValue(key, out var acc))
                        {
                            acc = new CompressionAccumulator();
                            buckets[key] = acc;
                        }
                        acc.N++;
                        acc.Expected += l.ExpectedPayment;
                        acc.Paid += l.ActualPaid;
                        acc.Variance += l.Variance;
                    }
                }

                return buckets
                    .Select(kv => new CompressionRow
                    {
                        Bucket = kv.Key,
                        Count = kv.Value.N,
                        Expected = kv.Value.Expected,
                        Paid = kv.Value.Paid,
                        Variance = kv.Value.Variance,
                        AvgCompressionPct = kv.Value.Expected == 0m ? 0m
                            : decimal.Round(kv.Value.Variance / kv.Value.Expected * 100m, 2),
                    })
                    .OrderByDescending(r => r.Variance)
                    .ToList();
            }
        }
    }

    private class CompressionAccumulator
    {
        public int N;
        public decimal Expected;
        public decimal Paid;
        public decimal Variance;
    }

    // -------------------------------------------------------------------
    // Filter applicators
    // -------------------------------------------------------------------
    private static IQueryable<ParsedClaim> ApplyClaimFilters(
        IQueryable<ParsedClaim> q, AnalyticsFilters f)
    {
        if (f.DateFrom.HasValue) q = q.Where(c => c.DateOfService != null && c.DateOfService >= f.DateFrom);
        if (f.DateTo.HasValue)   q = q.Where(c => c.DateOfService != null && c.DateOfService <= f.DateTo);
        if (!string.IsNullOrEmpty(f.PayerName))   q = q.Where(c => c.PayerName == f.PayerName);
        if (!string.IsNullOrEmpty(f.FileType))    q = q.Where(c => c.FileType == f.FileType);
        if (!string.IsNullOrEmpty(f.ProviderNpi)) q = q.Where(c => c.ProviderNpi == f.ProviderNpi);
        return q;
    }

    private static IQueryable<ApgResultRecord> ApplyClaimFiltersOnApg(
        IQueryable<ApgResultRecord> q, AnalyticsFilters f)
    {
        if (f.DateFrom.HasValue) q = q.Where(a => a.Claim!.DateOfService != null && a.Claim.DateOfService >= f.DateFrom);
        if (f.DateTo.HasValue)   q = q.Where(a => a.Claim!.DateOfService != null && a.Claim.DateOfService <= f.DateTo);
        if (!string.IsNullOrEmpty(f.PayerName))   q = q.Where(a => a.Claim!.PayerName == f.PayerName);
        if (!string.IsNullOrEmpty(f.FileType))    q = q.Where(a => a.Claim!.FileType == f.FileType);
        if (!string.IsNullOrEmpty(f.ProviderNpi)) q = q.Where(a => a.Claim!.ProviderNpi == f.ProviderNpi);
        return q;
    }

    private static IQueryable<ClaimAdjustment> ApplyClaimFiltersOnAdjustment(
        IQueryable<ClaimAdjustment> q, AnalyticsFilters f)
    {
        if (f.DateFrom.HasValue) q = q.Where(a => a.Claim!.DateOfService != null && a.Claim.DateOfService >= f.DateFrom);
        if (f.DateTo.HasValue)   q = q.Where(a => a.Claim!.DateOfService != null && a.Claim.DateOfService <= f.DateTo);
        if (!string.IsNullOrEmpty(f.PayerName))   q = q.Where(a => a.Claim!.PayerName == f.PayerName);
        if (!string.IsNullOrEmpty(f.FileType))    q = q.Where(a => a.Claim!.FileType == f.FileType);
        if (!string.IsNullOrEmpty(f.ProviderNpi)) q = q.Where(a => a.Claim!.ProviderNpi == f.ProviderNpi);
        return q;
    }
}
