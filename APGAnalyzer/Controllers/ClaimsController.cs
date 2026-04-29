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
    /// <summary>GET /Claims — paginated list of all parsed claims.</summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Single LEFT-JOIN-style projection to avoid loading service lines
        // for every claim on the list page. Limited to the most-recent 200
        // for now; full pagination + filters come in Session B.
        var rows = await db.ParsedClaims
            .OrderByDescending(c => c.CreatedAt)
            .Take(200)
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
                CreatedAt = c.CreatedAt,
            })
            .ToListAsync(ct);

        var total = await db.ParsedClaims.CountAsync(ct);

        return View(new ClaimsListViewModel { Rows = rows, TotalClaims = total });
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

        // Include the informational ICD-derived EAPG block (Phase 3 feature)
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
