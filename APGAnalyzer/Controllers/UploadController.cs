using APGAnalyzer.Models;
using APGAnalyzer.Services;
using APGAnalyzer.Services.Edi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APGAnalyzer.Controllers;

// Upload is an editing operation — viewers are blocked, admin + analyst allowed.
[Authorize(Roles = RoleSeeder.EditorRoles)]
public class UploadController(
    IClaimUploadService uploads,
    ICurrentUserContext currentUser,
    ILogger<UploadController> log) : Controller
{
    public IActionResult Index() => View(new UploadViewModel());

    /// <summary>
    /// POST /Upload — accept a batch of EDI files plus a family hint
    /// ("835" or "837"). Each file's specific subtype (I vs P) is auto-detected
    /// from the EDI itself. Per-file results are collected and rendered as
    /// a table on the same page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(200 * 1024 * 1024)]   // 200 MB total across all files
    public async Task<IActionResult> Upload(
        List<IFormFile> files, string family, CancellationToken ct)
    {
        var vm = new UploadViewModel { Family = family ?? "" };

        if (files is null || files.Count == 0 || files.All(f => f.Length == 0))
        {
            vm.ErrorMessage = "No files selected.";
            return View(nameof(Index), vm);
        }
        if (string.IsNullOrWhiteSpace(family) || (family != "835" && family != "837"))
        {
            vm.ErrorMessage = "Pick the file family (835 or 837) first.";
            return View(nameof(Index), vm);
        }

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var outcome = new UploadFileOutcome { FileName = file.FileName };
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();

                // 1. Auto-detect specific subtype
                var detection = EdiFileTypeDetector.Detect(bytes, family);
                outcome.DetectedType    = detection.FileType;
                outcome.Confidence      = detection.Confidence;
                outcome.DetectionReason = detection.Reason;

                // 2. Hand off to the existing parse+store+price pipeline.
                //    Stamp ownership with the SIGNED-IN user, not any view-as
                //    target — uploads always go into the uploader's bucket.
                outcome.Result = await uploads.ParseAndStoreAsync(
                    bytes, file.FileName, detection.FileType,
                    currentUser.SignedInUserId, ct);

                // Status: ok unless the parser produced warnings, or zero claims came out
                if (outcome.Result.ClaimsParsed == 0)
                {
                    outcome.Status = UploadFileStatus.Warning;
                    outcome.ErrorMessage =
                        "Parsed 0 claim(s). The file may be malformed or empty for this type.";
                }
                else if (outcome.Result.Warnings.Count > 0
                         || detection.Confidence == EdiFileTypeDetector.DetectionConfidence.Low)
                {
                    outcome.Status = UploadFileStatus.Warning;
                }
                else
                {
                    outcome.Status = UploadFileStatus.Ok;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Upload failed for {FileName}", file.FileName);
                outcome.Status = UploadFileStatus.Error;
                var msgs = new List<string>();
                for (var e = ex; e is not null; e = e.InnerException)
                    if (!string.IsNullOrWhiteSpace(e.Message)) msgs.Add(e.Message);
                outcome.ErrorMessage = string.Join("  →  ", msgs);
            }

            vm.Results.Add(outcome);
        }

        return View(nameof(Index), vm);
    }
}
