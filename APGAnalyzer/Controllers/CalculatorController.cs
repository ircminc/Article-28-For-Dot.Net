using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Services;
using APGAnalyzer.Services.Cms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

[Authorize]
public class CalculatorController(
    ApplicationDbContext db,
    IApgEngine engine,
    ICmsRateService cms,
    ICurrentUserContext currentUser,
    ILogger<CalculatorController> log) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new CalculatorViewModel();
        await PopulateLocalityDropdown(vm, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(CalculatorViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLocalityDropdown(vm, ct);
            return View(nameof(Index), vm);
        }

        // Filter to non-empty service lines
        var inputLines = vm.ServiceLines
            .Where(l => !string.IsNullOrWhiteSpace(l.ProcedureCode))
            .ToList();

        if (inputLines.Count == 0)
        {
            vm.ErrorMessage = "Add at least one service line with a CPT/HCPCS code.";
            await PopulateLocalityDropdown(vm, ct);
            return View(nameof(Index), vm);
        }

        // ---- APG path (existing behavior, unchanged math) ----------------
        ProviderConfig? provider = null;
        if (vm.ShouldComputeApg)
        {
            provider = await db.ProviderConfigs
                .OwnedBy(currentUser)
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (provider is null)
            {
                if (vm.Source == RateSource.Apg)
                {
                    vm.ErrorMessage = "APG calculation requires an active provider. "
                                    + "Please set one up in Provider Configuration first.";
                    await PopulateLocalityDropdown(vm, ct);
                    return View(nameof(Index), vm);
                }
                vm.Warning = (vm.Warning ?? "") + " APG skipped: no active provider configured.";
            }
            else
            {
                vm.ProviderConfigured = true;
                try
                {
                    var apgLines = inputLines
                        .Select((l, i) => new Models.Engine.ServiceLineDto
                        {
                            LineSeq = i + 1,
                            ProcedureCode = l.ProcedureCode!.Trim().ToUpperInvariant(),
                            Modifiers = SplitModifiers(l.Modifiers),
                            Units = Math.Max(1, l.Units),
                            PaidAmount = l.BilledAmount ?? 0m,
                            BilledAmount = l.BilledAmount ?? 0m,
                            AllowedAmount = l.BilledAmount ?? 0m,
                            DateOfService = vm.DateOfService,
                        })
                        .ToList();

                    var paidTotal = apgLines.Sum(l => l.PaidAmount);
                    var claim = new Models.Engine.ParsedClaimDto
                    {
                        ClaimId = $"CALC-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                        DateOfService = vm.DateOfService,
                        BilledAmount = paidTotal,
                        PaidAmount = paidTotal,
                        AllowedAmount = paidTotal,
                        PrincipalDiagnosis = DxCodeNormalizer.Normalize(vm.PrincipalDiagnosis),
                        ServiceLines = apgLines,
                    };

                    vm.Result = await engine.CalculateAsync(claim, provider, ct);

                    if (!string.IsNullOrWhiteSpace(vm.PrincipalDiagnosis))
                    {
                        vm.IcdBasedResult = await engine.ResolveIcdBasedEapgAsync(
                            vm.PrincipalDiagnosis, vm.DateOfService,
                            vm.Result.BaseRateApplied, ct);
                    }
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Rate Calculator (APG) failed");
                    vm.ErrorMessage = "APG calculation failed: " + ex.Message;
                }
            }
        }

        // ---- CMS path -----------------------------------------------------
        if (vm.ShouldComputeCms)
        {
            // Locality resolution: form > active provider > error
            var locality = vm.CmsLocality?.Trim();
            if (string.IsNullOrEmpty(locality))
            {
                provider ??= await db.ProviderConfigs
                    .OwnedBy(currentUser)
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(ct);
                locality = provider?.CmsLocality?.Trim();
            }

            if (string.IsNullOrEmpty(locality))
            {
                var msg = "CMS calculation requires a locality. Either pick one from the "
                        + "dropdown or set CmsLocality on your active provider.";
                if (vm.Source == RateSource.Cms)
                {
                    vm.ErrorMessage = msg;
                }
                else
                {
                    vm.Warning = (vm.Warning ?? "") + " CMS skipped: " + msg;
                }
            }
            else
            {
                vm.CmsResult = await ComputeCmsAsync(vm, inputLines, locality, ct);
            }
        }

        await PopulateLocalityDropdown(vm, ct);
        return View(nameof(Index), vm);
    }

    /// <summary>
    /// AJAX endpoint: refresh the locality dropdown for the requested year.
    /// Used when the user changes the date-of-service on the form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Localities(int year, CancellationToken ct)
    {
        var rows = await cms.ListLocalitiesAsync(year, ct);
        return Json(rows);
    }

    // -----------------------------------------------------------------
    // CMS calculation
    // -----------------------------------------------------------------

    private async Task<CmsCalculatorResult> ComputeCmsAsync(
        CalculatorViewModel vm,
        List<CalculatorLineInput> inputLines,
        string locality,
        CancellationToken ct)
    {
        var year = vm.DateOfService.Year;
        var result = new CmsCalculatorResult
        {
            Locality = locality,
            Year = year,
            UsedFacilityRate = vm.CmsUseFacilityRate,
        };

        // Process all lines; one line's error doesn't poison the rest
        for (int i = 0; i < inputLines.Count; i++)
        {
            var l = inputLines[i];
            var code = l.ProcedureCode!.Trim().ToUpperInvariant();
            var mods = SplitModifiers(l.Modifiers);
            var primaryMod = mods.Count > 0 ? mods[0] : "";
            var units = Math.Max(1, l.Units);

            var line = new CmsCalculatorLine
            {
                LineSeq       = i + 1,
                ProcedureCode = code,
                Modifier      = primaryMod,
                Units         = units,
                PaidAmount    = l.BilledAmount ?? 0m,
            };

            try
            {
                // Sequential lookups: the cache reads share a single DbContext,
                // which isn't thread-safe. Doing these in parallel via
                // Task.WhenAll triggers
                //   "A second operation was started on this context instance
                //    before a previous operation completed."
                // The CMS HTTP fetches are still parallel inside CmsRateService
                // when we hit a true cache miss (indicator + locality run via
                // Task.WhenAll there) — what's serial here is just the per-row
                // cache lookups, which are fast (B-tree seek on a unique index).
                var row = await cms.GetMpfsRateAsync(code, primaryMod, locality, year, ct: ct);
                Models.Domain.CmsRateCache? pro = null;
                Models.Domain.CmsRateCache? tec = null;
                if (vm.CmsIncludePcTc)
                {
                    pro = await cms.GetMpfsRateAsync(code, "26", locality, year, ct: ct);
                    tec = await cms.GetMpfsRateAsync(code, "TC", locality, year, ct: ct);
                }

                if (row is null)
                {
                    line.Error = $"No MPFS rate for {code}"
                        + (string.IsNullOrEmpty(primaryMod) ? "" : $" / mod {primaryMod}")
                        + $" / locality {locality} / year {year}.";
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

                    var chosen = vm.CmsUseFacilityRate ? row.FacilityRate : row.NonFacilityRate;
                    if (chosen.HasValue) line.ExpectedPayment = chosen.Value * units;

                    line.ProfessionalRate = pro?.NonFacilityRate;
                    line.TechnicalRate    = tec?.NonFacilityRate;
                }
            }
            catch (CmsDatasetMovedException ex)
            {
                // Catalog itself is unreachable — set a banner and stop early.
                result.Banner = $"CMS catalog unreachable: {ex.Message} "
                              + "Check outbound HTTPS to pfs.data.cms.gov, then retry.";
                log.LogError(ex, "CMS catalog moved/unreachable");
                line.Error = "CMS catalog unreachable.";
                result.Lines.Add(line);
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "CMS lookup failed for {Code}", code);
                line.Error = $"CMS API error: {ex.Message}";
            }

            result.Lines.Add(line);
        }

        return result;
    }

    private async Task PopulateLocalityDropdown(CalculatorViewModel vm, CancellationToken ct)
    {
        try
        {
            var year = vm.DateOfService.Year;
            ViewData["Localities"] = await cms.ListLocalitiesAsync(year, ct);
        }
        catch
        {
            ViewData["Localities"] = Array.Empty<CmsLocality>();
        }
    }

    private static List<string> SplitModifiers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(new[] { ',', ' ', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToList();
    }
}
