using APGAnalyzer.Models;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

/// <summary>
/// Reference-data administration. All actions require an authenticated
/// user — role-based gating (admin-only) comes in Session B alongside the
/// Master Reset endpoint.
/// </summary>
[Authorize]
public class SettingsController(
    ICrosswalkLoader crosswalkLoader,
    ILogger<SettingsController> log) : Controller
{
    public IActionResult Index() => View(new SettingsViewModel());

    /// <summary>
    /// POST /Settings/UploadCrosswalk
    /// Accepts an eMedNY APG Crosswalk .xlsx and replaces hcpcs_to_eapg +
    /// icd10_to_eapg in the database.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadCrosswalk(IFormFile file, CancellationToken ct)
    {
        var vm = new SettingsViewModel();

        if (file is null || file.Length == 0)
        {
            vm.ErrorContext = "Crosswalk";
            vm.ErrorMessage = "No file selected.";
            return View(nameof(Index), vm);
        }
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            vm.ErrorContext = "Crosswalk";
            vm.ErrorMessage = "File must be an Excel workbook (.xlsx or .xlsm).";
            return View(nameof(Index), vm);
        }

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            vm.CrosswalkResult = await crosswalkLoader.LoadFromBytesAsync(
                ms.ToArray(), file.FileName, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Crosswalk upload failed for file {FileName}", file.FileName);
            vm.ErrorContext = "Crosswalk";
            vm.ErrorMessage = ex.Message;
        }

        return View(nameof(Index), vm);
    }
}
