using System.Text.Json;
using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

[Authorize]
public class ClaimsController(
    ApplicationDbContext db,
    IApgEngine engine,
    ExportService exporter,
    ILogger<ClaimsController> log) : Controller
{
    /// <summary>
    /// GET /Claims with optional filter query string:
    ///   ?fileType=835I &amp; status=underpaid &amp; search=foo &amp; dosFrom=... &amp; dosTo=...
    ///   &amp; page=2 &amp; pageSize=50
    /// </summary>
    public async Task<IActionResult> Index(ClaimsListFilters filters, CancellationToken ct)
    {
        if (filters.PageSize <= 0 || filters.PageSize > 500) filters.PageSize = 50;
        if (filters.Page < 1) filters.Page = 1;

        var query = db.ParsedClaims.AsQueryable();

        if (!string.IsNullOrEmpty(filters.FileType))
            query = query.Where(c => c.FileType == filters.FileType);

        if (filters.DosFrom.HasValue)
            query = query.Where(c => c.DateOfService != null && c.DateOfService >= filters.DosFrom);
        if (filters.DosTo.HasValue)
            query = query.Where(c => c.DateOfService != null && c.DateOfService <= filters.DosTo);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var s = filters.Search.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.ClaimId, $"%{s}%") ||
                (c.PatientName != null && EF.Functions.Like(c.PatientName, $"%{s}%")) ||
                (c.PayerName != null && EF.Functions.Like(c.PayerName, $"%{s}%")) ||
                (c.ProviderNpi != null && EF.Functions.Like(c.ProviderNpi, $"%{s}%")));
        }

        if (!string.IsNullOrEmpty(filters.Status))
        {
            query = filters.Status.ToLowerInvariant() switch
            {
                "underpaid" => query.Where(c => c.ApgResult != null && c.ApgResult.Underpaid),
                "overpaid"  => query.Where(c => c.ApgResult != null && c.ApgResult.Overpaid),
                "match"     => query.Where(c => c.ApgResult != null
                                                && !c.ApgResult.Underpaid
                                                && !c.ApgResult.Overpaid),
                "unpriced"  => query.Where(c => c.ApgResult == null),
                _ => query,
            };
        }

        var totalFiltered = await query.CountAsync(ct);
        var totalUnfiltered = await db.ParsedClaims.CountAsync(ct);

        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .Select(c => new ClaimsListRow
            {
                Id = c.Id,
                ClaimId = c.ClaimId,
                FileType = c.FileType,
                DateOfService = c.DateOfService,
                PayerName = c.PayerName,
                PatientName = c.PatientName,
                BilledAmount = c.BilledAmount,
                PaidAmount = c.PaidAmount,
                CorrectApgPayment = c.ApgResult == null ? null : c.ApgResult.CorrectApgPayment,
                Variance = c.ApgResult == null ? null : c.ApgResult.Variance,
                Underpaid = c.ApgResult == null ? null : c.ApgResult.Underpaid,
                Overpaid = c.ApgResult == null ? null : c.ApgResult.Overpaid,
                IsLinked = c.LinkedClaimIdFk != null,
                CreatedAt = c.CreatedAt,
            })
            .ToListAsync(ct);

        return View(new ClaimsListViewModel
        {
            Rows = rows,
            TotalClaims = totalFiltered,
            TotalUnfiltered = totalUnfiltered,
            Filters = filters,
        });
    }

    /// <summary>GET /Claims/Detail/{id} — drill into one claim.</summary>
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var claim = await db.ParsedClaims
            .Include(c => c.ServiceLines)
            .Include(c => c.Adjustments)
            .Include(c => c.ApgResult)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return NotFound();

        var apg = claim.ApgResult;
        var vm = new ClaimDetailViewModel { Claim = claim, ApgResult = apg };

        // Hydrate line details + other diagnoses from JSON
        if (apg is not null && !string.IsNullOrEmpty(apg.LineDetailsJson))
        {
            try
            {
                vm.LineDetails = JsonSerializer.Deserialize<List<APGLineResult>>(apg.LineDetailsJson)
                                 ?? new List<APGLineResult>();
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Couldn't deserialize LineDetailsJson for claim {Id}", id);
            }
        }
        if (!string.IsNullOrEmpty(claim.OtherDiagnosesJson))
        {
            vm.OtherDiagnoses = JsonSerializer.Deserialize<List<string>>(claim.OtherDiagnosesJson)
                                ?? new();
        }

        // Linked sibling (837 ↔ 835)
        if (claim.LinkedClaimIdFk.HasValue)
        {
            vm.LinkedClaim = await db.ParsedClaims
                .FirstOrDefaultAsync(c => c.Id == claim.LinkedClaimIdFk.Value, ct);
        }

        // Informational ICD-derived EAPG (Phase 3 feature)
        if (!string.IsNullOrEmpty(claim.PrincipalDiagnosis) && claim.DateOfService.HasValue
            && apg is not null)
        {
            try
            {
                vm.IcdBasedResult = await engine.ResolveIcdBasedEapgAsync(
                    claim.PrincipalDiagnosis, claim.DateOfService.Value,
                    apg.BaseRateApplied, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "ICD-based EAPG lookup failed for claim {Id}", id);
            }
        }

        return View(vm);
    }

    /// <summary>
    /// GET /Claims/ExportXlsx (with the same filters as Index) — download
    /// the filtered claims list as an .xlsx.
    /// </summary>
    public async Task<IActionResult> ExportXlsx(ClaimsListFilters filters, CancellationToken ct)
    {
        // Re-use Index's filter logic by building the same query.
        var query = db.ParsedClaims.AsQueryable();
        if (!string.IsNullOrEmpty(filters.FileType))
            query = query.Where(c => c.FileType == filters.FileType);
        if (filters.DosFrom.HasValue)
            query = query.Where(c => c.DateOfService != null && c.DateOfService >= filters.DosFrom);
        if (filters.DosTo.HasValue)
            query = query.Where(c => c.DateOfService != null && c.DateOfService <= filters.DosTo);
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var s = filters.Search.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.ClaimId, $"%{s}%") ||
                (c.PatientName != null && EF.Functions.Like(c.PatientName, $"%{s}%")) ||
                (c.PayerName   != null && EF.Functions.Like(c.PayerName, $"%{s}%")) ||
                (c.ProviderNpi != null && EF.Functions.Like(c.ProviderNpi, $"%{s}%")));
        }
        if (!string.IsNullOrEmpty(filters.Status))
        {
            query = filters.Status.ToLowerInvariant() switch
            {
                "underpaid" => query.Where(c => c.ApgResult != null && c.ApgResult.Underpaid),
                "overpaid"  => query.Where(c => c.ApgResult != null && c.ApgResult.Overpaid),
                "match"     => query.Where(c => c.ApgResult != null
                                                && !c.ApgResult.Underpaid
                                                && !c.ApgResult.Overpaid),
                "unpriced"  => query.Where(c => c.ApgResult == null),
                _ => query,
            };
        }

        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClaimsListRow
            {
                Id = c.Id,
                ClaimId = c.ClaimId,
                FileType = c.FileType,
                DateOfService = c.DateOfService,
                PayerName = c.PayerName,
                PatientName = c.PatientName,
                BilledAmount = c.BilledAmount,
                PaidAmount = c.PaidAmount,
                CorrectApgPayment = c.ApgResult == null ? null : c.ApgResult.CorrectApgPayment,
                Variance = c.ApgResult == null ? null : c.ApgResult.Variance,
                Underpaid = c.ApgResult == null ? null : c.ApgResult.Underpaid,
                Overpaid = c.ApgResult == null ? null : c.ApgResult.Overpaid,
                IsLinked = c.LinkedClaimIdFk != null,
            })
            .ToListAsync(ct);

        var bytes = exporter.BuildClaimsListXlsx(rows);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"claims-{stamp}.xlsx");
    }

    /// <summary>GET /Claims/DetailXlsx/{id} — single-claim Excel export.</summary>
    public async Task<IActionResult> DetailXlsx(int id, CancellationToken ct)
    {
        var (claim, lines) = await LoadDetailAsync(id, ct);
        if (claim is null) return NotFound();

        var bytes = exporter.BuildClaimDetailXlsx(claim, claim.ApgResult, lines);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"claim-{Sanitize(claim.ClaimId)}.xlsx");
    }

    /// <summary>GET /Claims/DetailPdf/{id} — single-claim PDF export.</summary>
    public async Task<IActionResult> DetailPdf(int id, CancellationToken ct)
    {
        var (claim, lines) = await LoadDetailAsync(id, ct);
        if (claim is null) return NotFound();

        var bytes = exporter.BuildClaimDetailPdf(claim, claim.ApgResult, lines);
        return File(bytes, "application/pdf", $"claim-{Sanitize(claim.ClaimId)}.pdf");
    }

    /// <summary>GET /Claims/Cms1500/{id} — CMS-1500 form-shaped PDF.</summary>
    public async Task<IActionResult> Cms1500(int id, CancellationToken ct)
    {
        var (claim, lines) = await LoadDetailAsync(id, ct);
        if (claim is null) return NotFound();
        var bytes = exporter.BuildCms1500Pdf(claim, claim.ApgResult, lines);
        return File(bytes, "application/pdf", $"cms1500-{Sanitize(claim.ClaimId)}.pdf");
    }

    /// <summary>GET /Claims/Ub04/{id} — UB-04 form-shaped PDF.</summary>
    public async Task<IActionResult> Ub04(int id, CancellationToken ct)
    {
        var (claim, lines) = await LoadDetailAsync(id, ct);
        if (claim is null) return NotFound();
        var bytes = exporter.BuildUb04Pdf(claim, claim.ApgResult, lines);
        return File(bytes, "application/pdf", $"ub04-{Sanitize(claim.ClaimId)}.pdf");
    }

    /// <summary>
    /// GET /Claims/DataExport — config page with sheet checkboxes.
    /// Optional <paramref name="claimId"/> scopes to a single claim.
    /// </summary>
    public IActionResult DataExport(int? claimId, ClaimsListFilters? filters)
    {
        return View(new DataExportViewModel
        {
            ClaimId = claimId,
            Filters = filters ?? new ClaimsListFilters(),
        });
    }

    /// <summary>POST /Claims/DataExport — runs the configured export.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DataExport(DataExportViewModel vm, CancellationToken ct)
    {
        if (!vm.AnySheetSelected)
        {
            ModelState.AddModelError("", "Pick at least one sheet to include.");
            return View(vm);
        }

        var query = db.ParsedClaims
            .Include(c => c.ServiceLines)
            .Include(c => c.Adjustments)
            .Include(c => c.ApgResult)
            .AsQueryable();

        if (vm.ClaimId.HasValue)
        {
            query = query.Where(c => c.Id == vm.ClaimId.Value);
        }
        else
        {
            // Apply same filters as Index/ExportXlsx
            var f = vm.Filters;
            if (!string.IsNullOrEmpty(f.FileType))
                query = query.Where(c => c.FileType == f.FileType);
            if (f.DosFrom.HasValue)
                query = query.Where(c => c.DateOfService != null && c.DateOfService >= f.DosFrom);
            if (f.DosTo.HasValue)
                query = query.Where(c => c.DateOfService != null && c.DateOfService <= f.DosTo);
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim();
                query = query.Where(c =>
                    EF.Functions.Like(c.ClaimId, $"%{s}%") ||
                    (c.PatientName != null && EF.Functions.Like(c.PatientName, $"%{s}%")) ||
                    (c.PayerName   != null && EF.Functions.Like(c.PayerName, $"%{s}%")) ||
                    (c.ProviderNpi != null && EF.Functions.Like(c.ProviderNpi, $"%{s}%")));
            }
            if (!string.IsNullOrEmpty(f.Status))
            {
                query = f.Status.ToLowerInvariant() switch
                {
                    "underpaid" => query.Where(c => c.ApgResult != null && c.ApgResult.Underpaid),
                    "overpaid"  => query.Where(c => c.ApgResult != null && c.ApgResult.Overpaid),
                    "match"     => query.Where(c => c.ApgResult != null
                                                    && !c.ApgResult.Underpaid
                                                    && !c.ApgResult.Overpaid),
                    "unpriced"  => query.Where(c => c.ApgResult == null),
                    _ => query,
                };
            }
        }

        var claims = await query.OrderBy(c => c.Id).ToListAsync(ct);
        var bytes = exporter.BuildFullDataXlsx(claims, vm);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        var name = vm.ClaimId.HasValue
            ? $"claim-{Sanitize(claims.FirstOrDefault()?.ClaimId ?? "unknown")}-fulldata-{stamp}.xlsx"
            : $"claims-fulldata-{stamp}.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            name);
    }

    // -----------------------------------------------------------------
    // Shared loader for both single-claim exports
    // -----------------------------------------------------------------
    private async Task<(ParsedClaim? Claim, List<APGLineResult> Lines)> LoadDetailAsync(
        int id, CancellationToken ct)
    {
        var claim = await db.ParsedClaims
            .Include(c => c.ServiceLines)
            .Include(c => c.Adjustments)
            .Include(c => c.ApgResult)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return (null, new());

        var lines = new List<APGLineResult>();
        if (claim.ApgResult is { } apg && !string.IsNullOrEmpty(apg.LineDetailsJson))
        {
            try
            {
                lines = JsonSerializer.Deserialize<List<APGLineResult>>(apg.LineDetailsJson)
                        ?? new List<APGLineResult>();
            }
            catch { /* tolerate malformed cache */ }
        }
        return (claim, lines);
    }

    private static string Sanitize(string s)
        => string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}
