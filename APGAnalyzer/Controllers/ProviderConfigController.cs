using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

[Authorize(Roles = "admin")]
public class ProviderConfigController(ApplicationDbContext db, ILogger<ProviderConfigController> log) : Controller
{
    /// <summary>GET /ProviderConfig — show the active provider's settings.</summary>
    public async Task<IActionResult> Index()
    {
        var active = await db.ProviderConfigs.FirstOrDefaultAsync(x => x.IsActive);
        var vm = new ProviderConfigViewModel
        {
            ProviderName = active?.ProviderName ?? "",
            Npi = active?.Npi,
            CountyCode = active?.CountyCode,
            PeerGroup = active?.PeerGroup ?? "Clinic*",
            ProviderType = active?.ProviderType ?? "dtc",
            CapitalAddonEligible = active?.CapitalAddonEligible ?? false,
            CapitalAddonRate = active?.CapitalAddonRate,
            CurrentRegion = active?.Region,
        };
        await PopulateDropdowns(vm);
        return View(vm);
    }

    /// <summary>POST /ProviderConfig — save / replace the active provider.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProviderConfigViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdowns(vm);
            return View(nameof(Index), vm);
        }

        // Derive region from county. Engine.ResolveRegion would do this at
        // calculation time too, but stamping it on the row makes the
        // Settings UI show a stable "Current region" pill without an
        // extra query on each render.
        string? region = null;
        if (vm.CountyCode.HasValue)
        {
            var county = await db.ProviderCounties
                .FirstOrDefaultAsync(x => x.CountyCode == vm.CountyCode.Value, ct);
            region = county?.Region;
        }

        // Soft-replace: deactivate prior active row, insert new active row.
        var prior = await db.ProviderConfigs.Where(x => x.IsActive).ToListAsync(ct);
        foreach (var p in prior) p.IsActive = false;

        db.ProviderConfigs.Add(new ProviderConfig
        {
            IsActive = true,
            ProviderName = vm.ProviderName.Trim(),
            Npi = string.IsNullOrWhiteSpace(vm.Npi) ? null : vm.Npi.Trim(),
            CountyCode = vm.CountyCode,
            Region = region,
            PeerGroup = vm.PeerGroup.Trim(),
            ProviderType = vm.ProviderType.Trim(),
            CapitalAddonEligible = vm.CapitalAddonEligible,
            CapitalAddonRate = vm.CapitalAddonRate,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation("Provider config saved: {Name} / {Peer} / {Region}",
            vm.ProviderName, vm.PeerGroup, region);

        TempData["Success"] = $"Provider configuration saved (region: {region ?? "—"}).";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns(ProviderConfigViewModel vm)
    {
        vm.AllCounties = await db.ProviderCounties
            .OrderBy(x => x.CountyName)
            .Select(x => new ValueTuple<int, string, string>(x.CountyCode, x.CountyName, x.Region))
            .ToListAsync();

        vm.AllPeerGroups = await db.ApgBaseRates
            .Where(x => x.Source == "dtc")
            .Select(x => x.PeerGroup)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }
}
