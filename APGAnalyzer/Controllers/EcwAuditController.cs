using APGAnalyzer.Services.EcwAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

[Authorize]
public class EcwAuditController(
    IEcwAuditUploadService uploadSvc,
    IEcwAuditEngine        auditEngine,
    UserManager<IdentityUser> userMgr) : Controller
{
    // GET /EcwAudit
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = userMgr.GetUserId(User)!;
        var batches = await uploadSvc.GetBatchesAsync(userId, ct);
        return View(batches);
    }

    // GET /EcwAudit/Upload
    public IActionResult Upload() => View();

    // POST /EcwAudit/Upload
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        string practiceName,
        DateOnly auditDate,
        string? notes,
        IFormFile? file361,
        IFormFile? file371,
        IFormFile? file123,
        IFormFile? file1310,
        IFormFile? file3108,
        IFormFile? file3109Primary,
        IFormFile? file3109Secondary,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(practiceName))
        {
            ModelState.AddModelError("", "Practice name is required.");
            return View();
        }

        static Stream? ToStream(IFormFile? f) => f is { Length: > 0 } ? f.OpenReadStream() : null;

        var req = new EcwUploadRequest
        {
            PracticeName       = practiceName,
            AuditDate          = auditDate == default ? DateOnly.FromDateTime(DateTime.Today) : auditDate,
            Notes              = notes,
            File361            = ToStream(file361),
            File371            = ToStream(file371),
            File123            = ToStream(file123),
            File1310           = ToStream(file1310),
            File3108           = ToStream(file3108),
            File3109Primary    = ToStream(file3109Primary),
            File3109Secondary  = ToStream(file3109Secondary),
        };

        try
        {
            var userId = userMgr.GetUserId(User)!;
            var batch  = await uploadSvc.UploadAsync(req, userId, ct);
            TempData["EcwStatus"] = $"Uploaded successfully — batch #{batch.Id} for {batch.PracticeName}.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", $"Upload failed: {ex.Message}");
            return View();
        }
    }

    // GET /EcwAudit/Results/{id}
    public async Task<IActionResult> Results(int id, CancellationToken ct)
    {
        var batch = await uploadSvc.GetBatchAsync(id, ct);
        if (batch is null) return NotFound();
        var results = await auditEngine.RunAsync(id, ct);
        ViewBag.Batch = batch;
        return View(results);
    }

    // POST /EcwAudit/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await uploadSvc.DeleteBatchAsync(id, ct);
        TempData["EcwStatus"] = $"Audit batch #{id} deleted.";
        return RedirectToAction(nameof(Index));
    }
}
