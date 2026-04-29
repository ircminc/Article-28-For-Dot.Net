using System.Text.Json;
using APGAnalyzer.Data;
using APGAnalyzer.Models;
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
}
