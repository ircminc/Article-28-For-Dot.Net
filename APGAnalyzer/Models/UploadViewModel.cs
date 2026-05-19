using APGAnalyzer.Services;
using APGAnalyzer.Services.Edi;

namespace APGAnalyzer.Models;

public class UploadViewModel
{
    /// <summary>User-selected family: "835" or "837".</summary>
    public string Family { get; set; } = "835";

    /// <summary>One row per file processed in this submission.</summary>
    public List<UploadFileOutcome> Results { get; set; } = new();

    /// <summary>Top-level error (no files selected, etc.). Per-file errors live on the row.</summary>
    public string? ErrorMessage { get; set; }

    public int TotalClaimsParsed     => Results.Sum(r => r.Result?.ClaimsParsed     ?? 0);
    public int TotalApgResults       => Results.Sum(r => r.Result?.ApgResultsComputed ?? 0);
    public int FilesOk               => Results.Count(r => r.Status == UploadFileStatus.Ok);
    public int FilesWithErrors       => Results.Count(r => r.Status == UploadFileStatus.Error);
}

public enum UploadFileStatus { Ok, Warning, Error }

public class UploadFileOutcome
{
    public string FileName { get; set; } = "";
    public string DetectedType { get; set; } = "";    // 835I / 835P / 837I / 837P
    public EdiFileTypeDetector.DetectionConfidence Confidence { get; set; }
    public string DetectionReason { get; set; } = "";
    public ClaimUploadResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public UploadFileStatus Status { get; set; }
}
