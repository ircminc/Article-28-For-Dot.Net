using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services.EcwAudit;

public interface IEcwAuditEngine
{
    Task<List<AuditCheckResult>> RunAsync(int batchId, CancellationToken ct = default);
}

public class EcwAuditEngine(ApplicationDbContext db) : IEcwAuditEngine
{
    public async Task<List<AuditCheckResult>> RunAsync(int batchId, CancellationToken ct = default)
    {
        // Load all raw data for this batch up front
        var claims      = await db.EcwClaimFinancials.Where(x => x.BatchId == batchId).ToListAsync(ct);
        var cptLines    = await db.EcwCptLines.Where(x => x.BatchId == batchId).ToListAsync(ct);
        var submissions = await db.EcwSubmissions.Where(x => x.BatchId == batchId).ToListAsync(ct);
        var billingLags = await db.EcwBillingLags.Where(x => x.BatchId == batchId).ToListAsync(ct);
        var patientAging= await db.EcwPatientAgings.Where(x => x.BatchId == batchId).ToListAsync(ct);
        var payerAging  = await db.EcwPayerAgings.Where(x => x.BatchId == batchId && x.IsPrimary).ToListAsync(ct);

        return
        [
            Check1_Ncr(claims),
            Check2_DenialRate(claims),
            Check3_WriteoffRate(claims),
            Check4_CLag(billingLags),
            Check5_SubmissionLag(submissions),
            Check6_UnsubmittedClaims(claims, submissions),
            Check7_ResubmissionRate(submissions),
            Check8_PatientAr(patientAging),
            Check9_InsuranceAr(payerAging),
            Check10_CptMix(cptLines),
        ];
    }

    // ── Check 1: Net Collection Rate ─────────────────────────────────────────
    private static AuditCheckResult Check1_Ncr(List<EcwClaimFinancial> claims)
    {
        var active = claims.Where(c => !c.ClaimVoided).ToList();
        var totalBilled       = active.Sum(c => c.BilledCharge);
        var totalContractual  = active.Sum(c => c.ContractualAdjustment);
        var totalPayments     = active.Sum(c => c.Payments);
        var adjustedExpected  = totalBilled - totalContractual;
        var ncr = adjustedExpected > 0 ? totalPayments / adjustedExpected * 100m : 0m;

        var status = ncr >= 95m ? AuditStatus.Pass : ncr >= 85m ? AuditStatus.Warn : AuditStatus.Fail;
        return new AuditCheckResult
        {
            CheckId   = 1,
            CheckName = "Net Collection Rate (NCR)",
            Source    = "361.05",
            Formula   = "Payments ÷ (Billed − Contractual Adj) × 100",
            Benchmark = "≥ 95% Pass  |  85–94% Warn  |  < 85% Fail",
            Status    = status,
            Score     = $"{ncr:F1}%",
            Summary   = $"Collected {totalPayments:C0} of {adjustedExpected:C0} adjusted expected on {active.Count:N0} active claims.",
            DetailRows =
            [
                new("Total Claims (active)",        active.Count.ToString("N0")),
                new("Total Billed",                 totalBilled.ToString("C2")),
                new("Contractual Adjustments",      totalContractual.ToString("C2")),
                new("Adjusted Expected",            adjustedExpected.ToString("C2")),
                new("Total Payments Received",      totalPayments.ToString("C2")),
                new("Net Collection Rate",          $"{ncr:F2}%"),
            ],
        };
    }

    // ── Check 2: Denial Rate ─────────────────────────────────────────────────
    private static AuditCheckResult Check2_DenialRate(List<EcwClaimFinancial> claims)
    {
        var active = claims.Where(c => !c.ClaimVoided).ToList();
        var denied = active.Where(c =>
            (c.ClaimStatusGroupName ?? "").Contains("Denied", StringComparison.OrdinalIgnoreCase) ||
            (c.ClaimStatusCode ?? "").Contains("Denied", StringComparison.OrdinalIgnoreCase)).ToList();

        var rate = active.Count > 0 ? denied.Count / (decimal)active.Count * 100m : 0m;
        var status = rate <= 5m ? AuditStatus.Pass : rate <= 10m ? AuditStatus.Warn : AuditStatus.Fail;

        return new AuditCheckResult
        {
            CheckId   = 2,
            CheckName = "Denial Rate",
            Source    = "361.05",
            Formula   = "Denied Claims ÷ Total Active Claims × 100",
            Benchmark = "≤ 5% Pass  |  6–10% Warn  |  > 10% Fail",
            Status    = status,
            Score     = $"{rate:F1}%",
            Summary   = $"{denied.Count} denied claim(s) out of {active.Count} active claims.",
            DetailRows =
            [
                new("Total Active Claims", active.Count.ToString("N0")),
                new("Denied Claims",       denied.Count.ToString("N0")),
                new("Denial Rate",         $"{rate:F2}%"),
                new("Denied Billed $",     denied.Sum(c => c.BilledCharge).ToString("C2")),
            ],
            FlagRows = denied.Take(50).Select(c => new AuditFlagRow
            {
                ClaimNo    = c.ClaimNo,
                Patient    = c.Patient,
                ServiceDate= c.ServiceDate?.ToString("MM/dd/yyyy"),
                Payer      = c.PrimaryPayer,
                FlagDetail = c.ClaimStatusGroupName ?? c.ClaimStatusCode,
                Amount     = c.BilledCharge,
            }).ToList(),
        };
    }

    // ── Check 3: Write-off Rate ───────────────────────────────────────────────
    private static AuditCheckResult Check3_WriteoffRate(List<EcwClaimFinancial> claims)
    {
        var active  = claims.Where(c => !c.ClaimVoided).ToList();
        var billed  = active.Sum(c => c.BilledCharge);
        var writeoff= active.Sum(c => c.WriteoffAdjustment);
        var rate    = billed > 0 ? writeoff / billed * 100m : 0m;
        var status  = rate <= 5m ? AuditStatus.Pass : rate <= 10m ? AuditStatus.Warn : AuditStatus.Fail;

        var topWriteoffs = active
            .Where(c => c.WriteoffAdjustment > 0)
            .OrderByDescending(c => c.WriteoffAdjustment)
            .Take(20).ToList();

        return new AuditCheckResult
        {
            CheckId   = 3,
            CheckName = "Write-off Rate",
            Source    = "361.05",
            Formula   = "Write-off Adjustments ÷ Billed Charges × 100",
            Benchmark = "≤ 5% Pass  |  6–10% Warn  |  > 10% Fail",
            Status    = status,
            Score     = $"{rate:F1}%",
            Summary   = $"Total write-offs: {writeoff:C0} against {billed:C0} billed.",
            DetailRows =
            [
                new("Total Billed",        billed.ToString("C2")),
                new("Total Write-offs",    writeoff.ToString("C2")),
                new("Write-off Rate",      $"{rate:F2}%"),
                new("Claims with Write-offs", topWriteoffs.Count.ToString("N0")),
            ],
            FlagRows = topWriteoffs.Select(c => new AuditFlagRow
            {
                ClaimNo    = c.ClaimNo,
                Patient    = c.Patient,
                ServiceDate= c.ServiceDate?.ToString("MM/dd/yyyy"),
                Payer      = c.PrimaryPayer,
                FlagDetail = "Write-off",
                Amount     = c.WriteoffAdjustment,
            }).ToList(),
        };
    }

    // ── Check 4: C Lag (Chart-to-Claim) ──────────────────────────────────────
    private static AuditCheckResult Check4_CLag(List<EcwBillingLag> rows)
    {
        if (!rows.Any())
            return NoData(4, "C Lag (Chart → Claim Created)", "13.10");

        var withLag = rows.Where(r => r.DaysPnToClaimCreated.HasValue).ToList();
        if (!withLag.Any())
            return NoData(4, "C Lag (Chart → Claim Created)", "13.10");

        var avg = (decimal)withLag.Average(r => r.DaysPnToClaimCreated!.Value);
        var status = avg <= 3m ? AuditStatus.Pass : avg <= 7m ? AuditStatus.Warn : AuditStatus.Fail;

        var flagged = withLag.Where(r => r.DaysPnToClaimCreated > 7)
            .OrderByDescending(r => r.DaysPnToClaimCreated).Take(30).ToList();

        return new AuditCheckResult
        {
            CheckId   = 4,
            CheckName = "C Lag (Chart → Claim Created)",
            Source    = "13.10",
            Formula   = "Avg days from Progress Note Locked to Claim Created",
            Benchmark = "≤ 3 days Pass  |  4–7 days Warn  |  > 7 days Fail",
            Status    = status,
            Score     = $"{avg:F1} days",
            Summary   = $"Average {avg:F1} days from chart lock to claim creation across {withLag.Count:N0} encounters.",
            DetailRows =
            [
                new("Encounters Analyzed",     withLag.Count.ToString("N0")),
                new("Average C Lag",           $"{avg:F1} days"),
                new("Median C Lag",            $"{Median(withLag.Select(r => (decimal)r.DaysPnToClaimCreated!.Value)):F1} days"),
                new("Max C Lag",               $"{withLag.Max(r => r.DaysPnToClaimCreated!.Value)} days"),
                new("Encounters > 7 days",     flagged.Count.ToString("N0")),
            ],
            FlagRows = flagged.Select(r => new AuditFlagRow
            {
                ClaimNo    = r.ClaimNo,
                Patient    = r.PatientName,
                ServiceDate= r.AppointmentDate?.ToString("MM/dd/yyyy"),
                FlagDetail = $"{r.DaysPnToClaimCreated} days lag",
                Amount     = null,
            }).ToList(),
        };
    }

    // ── Check 5: Submission Lag ───────────────────────────────────────────────
    private static AuditCheckResult Check5_SubmissionLag(List<EcwSubmission> rows)
    {
        if (!rows.Any())
            return NoData(5, "Submission Lag (Claim Created → First Submitted)", "123.06");

        var withDates = rows
            .Where(r => r.ClaimDate.HasValue && r.ClaimFirstSubmissionDate.HasValue)
            .Select(r => (decimal)(r.ClaimFirstSubmissionDate!.Value.DayNumber - r.ClaimDate!.Value.DayNumber))
            .Where(d => d >= 0)
            .ToList();

        if (!withDates.Any())
            return NoData(5, "Submission Lag (Claim Created → First Submitted)", "123.06");

        var avg    = withDates.Average();
        var status = avg <= 2m ? AuditStatus.Pass : avg <= 5m ? AuditStatus.Warn : AuditStatus.Fail;

        return new AuditCheckResult
        {
            CheckId   = 5,
            CheckName = "Submission Lag (Claim → First Submitted)",
            Source    = "123.06",
            Formula   = "Avg days from Claim Created Date to First Submission Date",
            Benchmark = "≤ 2 days Pass  |  3–5 days Warn  |  > 5 days Fail",
            Status    = status,
            Score     = $"{avg:F1} days",
            Summary   = $"Average {avg:F1} days from claim creation to first submission across {withDates.Count:N0} claims.",
            DetailRows =
            [
                new("Claims Analyzed",     withDates.Count.ToString("N0")),
                new("Average Lag",         $"{avg:F1} days"),
                new("Median Lag",          $"{Median(withDates):F1} days"),
                new("Max Lag",             $"{withDates.Max():F0} days"),
                new("Claims > 5 days",     withDates.Count(d => d > 5).ToString("N0")),
            ],
        };
    }

    // ── Check 6: Unsubmitted Claims ───────────────────────────────────────────
    private static AuditCheckResult Check6_UnsubmittedClaims(
        List<EcwClaimFinancial> claims, List<EcwSubmission> submissions)
    {
        var submittedNos = submissions.Select(s => s.ClaimNo).Where(n => n != null).ToHashSet();
        var unsubmitted  = claims
            .Where(c => !c.ClaimVoided && c.Balance > 0 && !submittedNos.Contains(c.ClaimNo))
            .ToList();

        var status = unsubmitted.Count == 0 ? AuditStatus.Pass
                   : unsubmitted.Count <= 5  ? AuditStatus.Warn
                   : AuditStatus.Fail;

        return new AuditCheckResult
        {
            CheckId   = 6,
            CheckName = "Unsubmitted Claims",
            Source    = "361.05 × 123.06",
            Formula   = "Active claims with balance not found in submission log",
            Benchmark = "0 Pass  |  1–5 Warn  |  > 5 Fail",
            Status    = status,
            Score     = unsubmitted.Count.ToString("N0"),
            Summary   = unsubmitted.Count == 0
                ? "All active claims with balances have at least one submission record."
                : $"{unsubmitted.Count} claim(s) with outstanding balances have no submission record.",
            DetailRows =
            [
                new("Active Claims with Balance", claims.Count(c => !c.ClaimVoided && c.Balance > 0).ToString("N0")),
                new("Submission Records",         submissions.Select(s => s.ClaimNo).Distinct().Count().ToString("N0")),
                new("Unsubmitted Claims",         unsubmitted.Count.ToString("N0")),
                new("Unsubmitted Balance $",      unsubmitted.Sum(c => c.Balance).ToString("C2")),
            ],
            FlagRows = unsubmitted.Take(50).Select(c => new AuditFlagRow
            {
                ClaimNo    = c.ClaimNo,
                Patient    = c.Patient,
                ServiceDate= c.ServiceDate?.ToString("MM/dd/yyyy"),
                Payer      = c.PrimaryPayer,
                FlagDetail = "No submission record",
                Amount     = c.Balance,
            }).ToList(),
        };
    }

    // ── Check 7: Resubmission Rate ────────────────────────────────────────────
    private static AuditCheckResult Check7_ResubmissionRate(List<EcwSubmission> rows)
    {
        if (!rows.Any())
            return NoData(7, "Resubmission Rate", "123.06");

        var byClaimNo = rows
            .Where(r => r.ClaimNo != null)
            .GroupBy(r => r.ClaimNo!)
            .ToList();

        var total      = byClaimNo.Count;
        var resubmitted= byClaimNo.Where(g => g.Max(r => r.SubmissionCount) > 1).ToList();
        var rate       = total > 0 ? resubmitted.Count / (decimal)total * 100m : 0m;
        var status     = rate <= 10m ? AuditStatus.Pass : rate <= 20m ? AuditStatus.Warn : AuditStatus.Fail;

        return new AuditCheckResult
        {
            CheckId   = 7,
            CheckName = "Resubmission Rate",
            Source    = "123.06",
            Formula   = "Claims submitted > 1 time ÷ Total Claims × 100",
            Benchmark = "≤ 10% Pass  |  11–20% Warn  |  > 20% Fail",
            Status    = status,
            Score     = $"{rate:F1}%",
            Summary   = $"{resubmitted.Count} of {total} claims were submitted more than once.",
            DetailRows =
            [
                new("Total Claims",          total.ToString("N0")),
                new("Resubmitted Claims",    resubmitted.Count.ToString("N0")),
                new("Resubmission Rate",     $"{rate:F2}%"),
                new("Max Submissions (any)", rows.Max(r => r.SubmissionCount).ToString()),
            ],
        };
    }

    // ── Check 8: Patient AR > 90 Days ────────────────────────────────────────
    private static AuditCheckResult Check8_PatientAr(List<EcwPatientAging> rows)
    {
        if (!rows.Any())
            return NoData(8, "Patient AR > 90 Days", "31.08");

        var totalBalance = rows.Sum(r => r.Balance);
        var over90       = rows.Sum(r => r.Days91To120 + r.Days121To150 + r.Days151To180 + r.DaysOver180);
        var rate         = totalBalance > 0 ? over90 / totalBalance * 100m : 0m;
        var status       = rate <= 20m ? AuditStatus.Pass : rate <= 35m ? AuditStatus.Warn : AuditStatus.Fail;

        return new AuditCheckResult
        {
            CheckId   = 8,
            CheckName = "Patient AR > 90 Days",
            Source    = "31.08",
            Formula   = "(91–120 + 121–150 + 151–180 + >180 days) ÷ Total Patient Balance",
            Benchmark = "≤ 20% Pass  |  21–35% Warn  |  > 35% Fail",
            Status    = status,
            Score     = $"{rate:F1}%",
            Summary   = $"{over90:C0} of {totalBalance:C0} patient balance is aged over 90 days.",
            DetailRows =
            [
                new("Total Patient Balance",      totalBalance.ToString("C2")),
                new("Current (0–30 days)",        rows.Sum(r => r.Days0To30).ToString("C2")),
                new("31–60 days",                 rows.Sum(r => r.Days31To60).ToString("C2")),
                new("61–90 days",                 rows.Sum(r => r.Days61To90).ToString("C2")),
                new("91–120 days",                rows.Sum(r => r.Days91To120).ToString("C2")),
                new("121–150 days",               rows.Sum(r => r.Days121To150).ToString("C2")),
                new("151–180 days",               rows.Sum(r => r.Days151To180).ToString("C2")),
                new("> 180 days",                 rows.Sum(r => r.DaysOver180).ToString("C2")),
                new("Total > 90 Days",            over90.ToString("C2")),
                new("% > 90 Days",                $"{rate:F2}%"),
            ],
        };
    }

    // ── Check 9: Insurance AR > 90 Days ──────────────────────────────────────
    private static AuditCheckResult Check9_InsuranceAr(List<EcwPayerAging> rows)
    {
        if (!rows.Any())
            return NoData(9, "Insurance AR > 90 Days (Primary)", "31.09 Primary");

        var totalBalance = rows.Sum(r => r.Balance);
        var over90       = rows.Sum(r => r.Days91To120 + r.DaysOver120);
        var rate         = totalBalance > 0 ? over90 / totalBalance * 100m : 0m;
        var status       = rate <= 15m ? AuditStatus.Pass : rate <= 30m ? AuditStatus.Warn : AuditStatus.Fail;

        var topPayers = rows
            .GroupBy(r => r.PayerName ?? "Unknown")
            .Select(g => new { Payer = g.Key, Balance = g.Sum(r => r.Balance), Over90 = g.Sum(r => r.Days91To120 + r.DaysOver120) })
            .OrderByDescending(x => x.Over90).Take(10).ToList();

        return new AuditCheckResult
        {
            CheckId   = 9,
            CheckName = "Insurance AR > 90 Days (Primary)",
            Source    = "31.09 Primary",
            Formula   = "(91–120 + >120 days) ÷ Total Insurance Balance",
            Benchmark = "≤ 15% Pass  |  16–30% Warn  |  > 30% Fail",
            Status    = status,
            Score     = $"{rate:F1}%",
            Summary   = $"{over90:C0} of {totalBalance:C0} primary insurance balance is aged over 90 days.",
            DetailRows = new List<AuditDetailRow>
            {
                new("Total Insurance Balance", totalBalance.ToString("C2")),
                new("Current (0–30 days)",     rows.Sum(r => r.DaysCurrent).ToString("C2")),
                new("31–60 days",              rows.Sum(r => r.Days31To60).ToString("C2")),
                new("61–90 days",              rows.Sum(r => r.Days61To90).ToString("C2")),
                new("91–120 days",             rows.Sum(r => r.Days91To120).ToString("C2")),
                new("> 120 days",              rows.Sum(r => r.DaysOver120).ToString("C2")),
                new("Total > 90 Days",         over90.ToString("C2")),
                new("% > 90 Days",             $"{rate:F2}%"),
            }.Concat(topPayers.Select(p => new AuditDetailRow($"  {p.Payer}", $"{p.Over90:C2} of {p.Balance:C2} > 90d")))
             .ToList(),
        };
    }

    // ── Check 10: Top CPT Mix ─────────────────────────────────────────────────
    private static AuditCheckResult Check10_CptMix(List<EcwCptLine> lines)
    {
        if (!lines.Any())
            return NoData(10, "Top CPT Procedure Mix", "371.05");

        var topCpts = lines
            .GroupBy(l => new { l.CptCode, l.CptDescription })
            .Select(g => new
            {
                g.Key.CptCode,
                g.Key.CptDescription,
                Count         = g.Count(),
                TotalBilled   = g.Sum(l => l.BilledCharge),
                TotalPayment  = g.Sum(l => l.TotalPayment),
                AvgPayment    = g.Average(l => l.TotalPayment),
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList();

        return new AuditCheckResult
        {
            CheckId   = 10,
            CheckName = "Top CPT Procedure Mix",
            Source    = "371.05",
            Formula   = "Top 10 CPT codes by billed volume — informational",
            Benchmark = "Informational only",
            Status    = AuditStatus.Info,
            Score     = $"{lines.Count:N0} lines",
            Summary   = $"Top 10 CPT codes account for {topCpts.Sum(x => x.Count):N0} of {lines.Count:N0} total line items.",
            DetailRows = topCpts.Select((c, i) =>
                new AuditDetailRow(
                    $"#{i+1} {c.CptCode} — {(c.CptDescription ?? "").Split(' ').FirstOrDefault()}",
                    $"{c.Count:N0} claims  |  Avg pymt {c.AvgPayment:C2}  |  Total {c.TotalPayment:C0}")
            ).ToList(),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static AuditCheckResult NoData(int id, string name, string source) =>
        new()
        {
            CheckId   = id,
            CheckName = name,
            Source    = source,
            Formula   = "",
            Benchmark = "",
            Status    = AuditStatus.Info,
            Score     = "No data",
            Summary   = $"No {source} data was uploaded for this batch.",
        };

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0m;
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2m : sorted[mid];
    }
}
