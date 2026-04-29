using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Engine;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

[Authorize]
public class CalculatorController(
    ApplicationDbContext db,
    IApgEngine engine,
    ILogger<CalculatorController> log) : Controller
{
    public IActionResult Index() => View(new CalculatorViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(CalculatorViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(nameof(Index), vm);

        // 1. Active provider
        var provider = await db.ProviderConfigs
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (provider is null)
        {
            vm.ErrorMessage = "No active provider configured. "
                            + "Please set one up in Provider Configuration first.";
            return View(nameof(Index), vm);
        }
        vm.ProviderConfigured = true;

        // 2. Build a synthetic claim from the form inputs
        var lines = vm.ServiceLines
            .Where(l => !string.IsNullOrWhiteSpace(l.ProcedureCode))
            .Select((l, i) => new ServiceLineDto
            {
                LineSeq = i + 1,
                ProcedureCode = l.ProcedureCode!.Trim().ToUpperInvariant(),
                Modifiers = SplitModifiers(l.Modifiers),
                Units = Math.Max(1, l.Units),
                PaidAmount = l.BilledAmount ?? 0,
                BilledAmount = l.BilledAmount ?? 0,
                AllowedAmount = l.BilledAmount ?? 0,
                DateOfService = vm.DateOfService,
            })
            .ToList();
        if (lines.Count == 0)
        {
            vm.ErrorMessage = "Add at least one service line with a CPT/HCPCS code.";
            return View(nameof(Index), vm);
        }

        var paidTotal = lines.Sum(l => l.PaidAmount);
        var claim = new ParsedClaimDto
        {
            ClaimId = $"CALC-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            DateOfService = vm.DateOfService,
            BilledAmount = paidTotal,
            PaidAmount = paidTotal,
            AllowedAmount = paidTotal,
            PrincipalDiagnosis = DxCodeNormalizer.Normalize(vm.PrincipalDiagnosis),
            ServiceLines = lines,
        };

        try
        {
            // 3. APG calculation (per-line HCPCS-driven payment)
            vm.Result = await engine.CalculateAsync(claim, provider, ct);

            // 4. ICD-derived informational EAPG (if a dx was supplied)
            if (!string.IsNullOrWhiteSpace(vm.PrincipalDiagnosis))
            {
                vm.IcdBasedResult = await engine.ResolveIcdBasedEapgAsync(
                    vm.PrincipalDiagnosis, vm.DateOfService,
                    vm.Result.BaseRateApplied, ct);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Rate Calculator failed");
            vm.ErrorMessage = ex.Message;
        }

        return View(nameof(Index), vm);
    }

    private static List<string> SplitModifiers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries
                                                  | StringSplitOptions.TrimEntries)
                  .Select(s => s.ToUpperInvariant())
                  .ToList();
    }
}
