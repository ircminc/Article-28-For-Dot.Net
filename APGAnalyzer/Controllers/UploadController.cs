using APGAnalyzer.Models;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

[Authorize]
public class UploadController(IClaimUploadService uploads, ILogger<UploadController> log) : Controller
{
    public IActionResult Index() => View(new UploadViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, string fileType, CancellationToken ct)
    {
        var vm = new UploadViewModel { FileType = fileType };

        if (file is null || file.Length == 0)
        {
            vm.ErrorMessage = "No file selected.";
            return View(nameof(Index), vm);
        }
        if (string.IsNullOrEmpty(fileType))
        {
            vm.ErrorMessage = "File type not selected.";
            return View(nameof(Index), vm);
        }

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            vm.Result = await uploads.ParseAndStoreAsync(
                ms.ToArray(), file.FileName, fileType, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Upload failed for {FileName}", file.FileName);
            // Walk the inner exception chain so the user sees the real cause.
            var msgs = new List<string>();
            for (var e = ex; e is not null; e = e.InnerException)
                if (!string.IsNullOrWhiteSpace(e.Message)) msgs.Add(e.Message);
            vm.ErrorMessage = string.Join("  →  ", msgs);
        }
        return View(nameof(Index), vm);
    }
}
