using APGAnalyzer.Models;
using APGAnalyzer.Services;
using APGAnalyzer.Services.Cms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

/// <summary>
/// Reference-data administration. Requires the "admin" role — Identity
/// roles are seeded by RoleSeeder on startup and the first registered user
/// is auto-promoted, so admin@test.com (or whoever signed up first)
/// already has access without any manual claim wiring.
/// </summary>
[Authorize(Roles = "admin")]
public class SettingsController(
    ICrosswalkLoader crosswalk,
    IWeightsHistoryLoader weights,
    IPmtacFeeCalculatorLoader pmtac,
    IDtcBaseRatesLoader dtc,
    IMasterResetService masterReset,
    ICmsRateService cms,
    ILogger<SettingsController> log) : Controller
{
    public IActionResult Index() => View(new SettingsViewModel());

    // -----------------------------------------------------------------
    // 1. APG Crosswalk (.xlsx)
    // -----------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadCrosswalk(IFormFile file, CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        if (!ValidateFile(file, vm, "Crosswalk", ".xlsx", ".xlsm")) return View(nameof(Index), vm);
        try
        {
            var bytes = await ReadAll(file, ct);
            vm.CrosswalkResult = await crosswalk.LoadFromBytesAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex) { Fail(vm, "Crosswalk", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // 2. Weights + Px + Fee Schedule (.xls)
    // -----------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadWeightsHistory(IFormFile file, CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        if (!ValidateFile(file, vm, "Weights+Fees", ".xls", ".xlsx", ".xlsm")) return View(nameof(Index), vm);
        try
        {
            var bytes = await ReadAll(file, ct);
            vm.WeightsHistoryResult = await weights.LoadFromBytesAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex) { Fail(vm, "Weights+Fees", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // 3. PMTAC Updated APG Fee Calculator (.xlsx)
    // -----------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadPmtac(IFormFile file, CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        if (!ValidateFile(file, vm, "PMTAC Fee Calculator", ".xlsx", ".xlsm")) return View(nameof(Index), vm);
        try
        {
            var bytes = await ReadAll(file, ct);
            vm.PmtacResult = await pmtac.LoadFromBytesAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex) { Fail(vm, "PMTAC Fee Calculator", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // 4. NYS DOH DTC base-rates inventory (.xls)
    // -----------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadDtc(IFormFile file, CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        if (!ValidateFile(file, vm, "DTC base rates", ".xls", ".xlsx", ".xlsm")) return View(nameof(Index), vm);
        try
        {
            var bytes = await ReadAll(file, ct);
            vm.DtcResult = await dtc.LoadFromBytesAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex) { Fail(vm, "DTC base rates", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // 5. Master Reset — wipes every reference table
    // -----------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MasterReset(string? confirmText, CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        if (!string.Equals(confirmText, "RESET", StringComparison.Ordinal))
        {
            vm.ErrorContext = "Master Reset";
            vm.ErrorMessage = "Confirmation text must equal RESET (case-sensitive).";
            return View(nameof(Index), vm);
        }
        try { vm.MasterResetResult = await masterReset.ResetAsync(ct); }
        catch (Exception ex) { Fail(vm, "Master Reset", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // 6. CMS Medicare cache refresh
    // -----------------------------------------------------------------
    /// <summary>
    /// POST /Settings/RefreshCmsCache — admin-only. Clears the in-process
    /// catalog + locality caches and marks every cms_rate_cache row stale.
    /// The next rate lookup for any HCPCS will re-fetch from CMS.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshCmsCache(CancellationToken ct)
    {
        var vm = new SettingsViewModel();
        try
        {
            vm.CmsRefreshResult = await cms.RefreshCacheAsync(ct);
            log.LogInformation("Admin {Admin} refreshed CMS cache: {Stale} row(s)",
                User.Identity?.Name, vm.CmsRefreshResult.RowsMarkedStale);
        }
        catch (Exception ex) { Fail(vm, "CMS Refresh", ex); }
        return View(nameof(Index), vm);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------
    private static async Task<byte[]> ReadAll(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private bool ValidateFile(IFormFile? file, SettingsViewModel vm, string context,
                               params string[] allowedExts)
    {
        if (file is null || file.Length == 0)
        {
            vm.ErrorContext = context;
            vm.ErrorMessage = "No file selected.";
            return false;
        }
        if (!allowedExts.Any(ext =>
                file.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
        {
            vm.ErrorContext = context;
            vm.ErrorMessage = $"File must be one of: {string.Join(", ", allowedExts)}.";
            return false;
        }
        return true;
    }

    private void Fail(SettingsViewModel vm, string context, Exception ex)
    {
        log.LogError(ex, "{Context} upload failed", context);
        vm.ErrorContext = context;
        // Walk the inner-exception chain so the most useful SQL error
        // (e.g. "Cannot insert explicit value for identity column...")
        // surfaces in the UI instead of EF Core's generic
        // "An error occurred while saving the entity changes" wrapper.
        var msgs = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(e.Message))
                msgs.Add(e.Message);
        }
        vm.ErrorMessage = string.Join("  →  ", msgs);
    }
}
