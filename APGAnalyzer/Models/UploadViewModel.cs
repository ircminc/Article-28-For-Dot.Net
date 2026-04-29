using APGAnalyzer.Services;

namespace APGAnalyzer.Models;

public class UploadViewModel
{
    public string FileType { get; set; } = "835I";
    public ClaimUploadResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
}
