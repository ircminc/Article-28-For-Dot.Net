using APGAnalyzer.Services;

namespace APGAnalyzer.Models;

/// <summary>
/// Drives the Settings page. Carries any flash-style success/error info
/// from the most recent upload back to the UI for display.
/// </summary>
public class SettingsViewModel
{
    public CrosswalkLoadResult? CrosswalkResult { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorContext { get; set; }   // which card the error came from
}
