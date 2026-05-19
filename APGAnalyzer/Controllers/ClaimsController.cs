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
    APGAnalyzer.Services.Cms.ICmsRateService cms,
    ICurrentUserContext currentUser,
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

        var query = ApplyClaimFilters(
            db.ParsedClaims.OwnedBy(currentUser), filters, null);

        var totalFiltered = await query.CountAsync(ct);
        var totalUnfiltered = await db.ParsedClaims.OwnedBy(currentUser).CountAsync(ct);

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
                OwnerUserId = c.OwnerUserId,
            })
            .ToListAsync(ct);

        // Hydrate owner emails — only fetch the unique set of ids in this page.
        // (Skipped when no row has an owner; cheap when most rows share an owner.)
        var ownerIds = rows.Where(r => r.OwnerUserId != null)
                           .Select(r => r.OwnerUserId!)
                           .Distinct()
                           .ToList();
        if (ownerIds.Count > 0)
        {
            var emailMap = await db.Users
                .Where(u => ownerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
            foreach (var r in rows)
            {
                if (r.OwnerUserId is not null && emailMap.TryGetValue(r.OwnerUserId, out var email))
                    r.OwnerEmail = email;
            }
        }

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
            .OwnedBy(currentUser)
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

        // Linked sibling (837 ↔ 835). Sibling shares the owner since pairs
        // are always uploaded by the same user, but we still scope for safety.
        if (claim.LinkedClaimIdFk.HasValue)
        {
            vm.LinkedClaim = await db.ParsedClaims
                .OwnedBy(currentUser)
                .FirstOrDefaultAsync(c => c.Id == claim.LinkedClaimIdFk.Value, ct);
        }

        // CMS Medicare comparison for professional claims only. Institutional
        // claims don't typically get billed against MPFS — they go through APC/APG.
        if ((claim.FileType == "837P" || claim.FileType == "835P")
            && claim.DateOfService.HasValue
            && claim.ServiceLines.Count > 0)
        {
            vm.CmsResult = await BuildCmsComparisonAsync(claim, ct);
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
    /// the filtered claims list as an .xlsx. If <paramref name="selectedIds"/>
    /// is non-empty, it scopes the export to exactly those claims (filters
    /// are ignored in that case).
    /// </summary>
    public async Task<IActionResult> ExportXlsx(
        ClaimsListFilters filters, int[]? selectedIds, CancellationToken ct)
    {
        var query = ApplyClaimFilters(
            db.ParsedClaims.OwnedBy(currentUser), filters, selectedIds);

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
    /// Optional <paramref name="selectedIds"/> scopes to multiple selected claims.
    /// </summary>
    public IActionResult DataExport(int? claimId, ClaimsListFilters? filters, int[]? selectedIds)
    {
        return View(new DataExportViewModel
        {
            ClaimId = claimId,
            Filters = filters ?? new ClaimsListFilters(),
            SelectedIds = selectedIds?.ToList() ?? new List<int>(),
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
            .OwnedBy(currentUser)
            .Include(c => c.ServiceLines)
            .Include(c => c.Adjustments)
            .Include(c => c.ApgResult)
            .AsQueryable();

        if (vm.ClaimId.HasValue)
        {
            query = query.Where(c => c.Id == vm.ClaimId.Value);
        }
        else if (vm.SelectedIds.Count > 0)
        {
            var ids = vm.SelectedIds;
            query = query.Where(c => ids.Contains(c.Id));
        }
        else
        {
            // Apply same filters as Index/ExportXlsx
            query = ApplyClaimFilters(query, vm.Filters, null);
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
            .OwnedBy(currentUser)
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

    /// <summary>
    /// Build a CMS Medicare per-line comparison for a 837P/835P claim. Looks
    /// up locality from the active provider config (per-user); returns null
    /// when no locality is configured. Catches the catalog-moved exception
    /// so a CMS API outage doesn't break the claim detail page.
    /// </summary>
    private async Task<CmsCalculatorResult?> BuildCmsComparisonAsync(
        ParsedClaim claim, CancellationToken ct)
    {
        // Locality from the claim's owner's active provider config.
        var ownerId = claim.OwnerUserId;
        if (string.IsNullOrEmpty(ownerId)) return null;

        var provider = await db.ProviderConfigs
            .Where(p => p.OwnerUserId == ownerId && p.IsActive)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        var locality = provider?.CmsLocality?.Trim();
        if (string.IsNullOrEmpty(locality)) return null;

        var year = claim.DateOfService!.Value.Year;
        var result = new CmsCalculatorResult
        {
            Locality = locality,
            Year = year,
            UsedFacilityRate = false,    // outpatient professional defaults to non-facility
        };

        foreach (var sl in claim.ServiceLines.OrderBy(x => x.LineSeq))
        {
            if (string.IsNullOrWhiteSpace(sl.ProcedureCode)) continue;

            // First modifier from the JSON list (if any)
            var primaryMod = "";
            if (!string.IsNullOrEmpty(sl.ModifiersJson))
            {
                try
                {
                    var mods = System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(sl.ModifiersJson) ?? new();
                    primaryMod = mods.Count > 0 ? mods[0] : "";
                }
                catch { /* tolerate malformed JSON */ }
            }

            var line = new CmsCalculatorLine
            {
                LineSeq       = sl.LineSeq,
                ProcedureCode = sl.ProcedureCode.ToUpperInvariant(),
                Modifier      = primaryMod,
                Units         = sl.Units > 0 ? sl.Units : 1,
                PaidAmount    = sl.PaidAmount,
            };

            try
            {
                var row = await cms.GetMpfsRateAsync(
                    line.ProcedureCode, primaryMod, locality, year, ct: ct);
                if (row is null)
                {
                    line.Error = $"No MPFS rate for {line.ProcedureCode}/{primaryMod}/{locality}/{year}.";
                }
                else
                {
                    line.NonFacilityRate = row.NonFacilityRate;
                    line.FacilityRate    = row.FacilityRate;
                    line.WorkRvu         = row.WorkRvu;
                    line.PeRvu           = row.PeRvu;
                    line.MpRvu           = row.MpRvu;
                    line.TotalRvu        = row.TotalRvu;
                    line.ConversionFactor = row.ConversionFactor;
                    if (row.NonFacilityRate.HasValue)
                        line.ExpectedPayment = row.NonFacilityRate.Value * line.Units;
                }
            }
            catch (APGAnalyzer.Services.Cms.CmsDatasetMovedException ex)
            {
                result.Banner = $"CMS catalog unreachable: {ex.Message}";
                line.Error = "CMS API unreachable.";
                result.Lines.Add(line);
                return result;   // bail early, no point hammering on the rest
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "CMS comparison failed for line {Code}", line.ProcedureCode);
                line.Error = $"CMS API error: {ex.Message}";
            }

            result.Lines.Add(line);
        }

        return result;
    }

    /// <summary>
    /// Shared filter applicator. If <paramref name="selectedIds"/> is non-empty,
    /// filters are skipped and only claims with those IDs are returned.
    /// </summary>
    private static IQueryable<ParsedClaim> ApplyClaimFilters(
        IQueryable<ParsedClaim> query, ClaimsListFilters filters, int[]? selectedIds)
    {
        if (selectedIds is { Length: > 0 })
        {
            return query.Where(c => selectedIds.Contains(c.Id));
        }

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
        return query;
    }

    /// <summary>
    /// POST /Claims/Delete — delete a single claim. Wraps DeleteSelected.
    /// Editor-only (viewers cannot delete).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleSeeder.EditorRoles)]
    public Task<IActionResult> Delete(int id, CancellationToken ct)
        => DeleteSelected(new[] { id }, ct);

    /// <summary>
    /// POST /Claims/DeleteSelected — delete every claim whose ID is in
    /// <paramref name="selectedIds"/>. Cascades clean up service lines,
    /// adjustments and APG results automatically (DeleteBehavior.Cascade).
    /// The 835↔837 self-link is NoAction, so we null those out first to
    /// avoid a referential-integrity violation. Editor-only.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = RoleSeeder.EditorRoles)]
    public async Task<IActionResult> DeleteSelected(int[] selectedIds, CancellationToken ct)
    {
        if (selectedIds is null || selectedIds.Length == 0)
        {
            TempData["ClaimsError"] = "No claims were selected for deletion.";
            return RedirectToAction(nameof(Index));
        }

        // 1. Null out LinkedClaimIdFk on EVERY affected row before any
        //    delete: both the rows we're about to delete (so a 837↔835
        //    pair selected together doesn't create a circular FK dep)
        //    AND any external siblings that point to a selected row
        //    (so the NoAction FK doesn't block the delete). Save this
        //    in its own round-trip so the DB sees the unlinks first.
        //
        //    Both queries are scoped via OwnedBy(currentUser): an analyst
        //    can only ever target their own claims; admins viewing-as see
        //    only that user's claims; unscoped admin sees everything.
        var victims = await db.ParsedClaims
            .OwnedBy(currentUser)
            .Where(c => selectedIds.Contains(c.Id))
            .ToListAsync(ct);
        if (victims.Count == 0)
        {
            TempData["ClaimsError"] = "Selected claims were not found (already deleted?).";
            return RedirectToAction(nameof(Index));
        }

        var externalSiblings = await db.ParsedClaims
            .OwnedBy(currentUser)
            .Where(c => c.LinkedClaimIdFk != null
                        && selectedIds.Contains(c.LinkedClaimIdFk.Value)
                        && !selectedIds.Contains(c.Id))
            .ToListAsync(ct);

        foreach (var v in victims)            v.LinkedClaimIdFk = null;
        foreach (var s in externalSiblings)   s.LinkedClaimIdFk = null;
        await db.SaveChangesAsync(ct);

        // 2. Now delete the selected claims (children cascade).
        db.ParsedClaims.RemoveRange(victims);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Deleted {Count} claim(s): [{Ids}]",
            victims.Count, string.Join(",", victims.Select(v => v.Id)));

        TempData["ClaimsStatus"] =
            $"Deleted {victims.Count} claim(s). "
            + (externalSiblings.Count > 0
                ? $"{externalSiblings.Count} external linked sibling(s) were unlinked."
                : "");
        return RedirectToAction(nameof(Index));
    }
}
