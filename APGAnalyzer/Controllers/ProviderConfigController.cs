using APGAnalyzer.Data;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Services;
using APGAnalyzer.Services.Cms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

// Provider settings are editable by admin AND analyst (per user request).
// Viewer remains read-blocked.
[Authorize(Roles = RoleSeeder.EditorRoles)]
public class ProviderConfigController(
    ApplicationDbContext db,
    ICurrentUserContext currentUser,
    ICmsRateService cms,
    ILogger<ProviderConfigController> log) : Controller
{
    /// <summary>GET /ProviderConfig — show the active provider's settings.
    /// Each user has their own active provider; admins viewing-as-someone
    /// see that user's provider.</summary>
    public async Task<IActionResult> Index()
    {
        var active = await db.ProviderConfigs
            .OwnedBy(currentUser)
            .FirstOrDefaultAsync(x => x.IsActive);
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
            CmsLocality = active?.CmsLocality,
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

        // Soft-replace: deactivate prior active row(s) FOR THIS USER, then
        // insert new active row stamped with the same owner. Other users'
        // configs are not touched — each user has their own active provider.
        var ownerId = currentUser.SignedInUserId;
        var prior = await db.ProviderConfigs
            .Where(x => x.IsActive && x.OwnerUserId == ownerId)
            .ToListAsync(ct);
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
            CmsLocality = string.IsNullOrWhiteSpace(vm.CmsLocality) ? null : vm.CmsLocality.Trim(),
            UpdatedAt = DateTime.UtcNow,
            OwnerUserId = ownerId,
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

        // CMS locality dropdown — live from pfs.data.cms.gov, cached 24h.
        // We use the current year for the locality list. Localities barely
        // change year-to-year, so this is fine even when picking for older DOS.
        try
        {
            vm.AllCmsLocalities = await cms.ListLocalitiesAsync(DateTime.UtcNow.Year);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CMS locality list fetch failed; falling back to free-text input.");
            vm.AllCmsLocalities = Array.Empty<APGAnalyzer.Services.Cms.CmsLocality>();
        }
    }
}
