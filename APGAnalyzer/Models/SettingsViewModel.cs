using APGAnalyzer.Services;
using APGAnalyzer.Services.Cms;

namespace APGAnalyzer.Models;

/// <summary>
/// Drives the Settings page. Carries flash-style success/error info from
/// the most recent admin action back to the UI for display.
/// </summary>
public class SettingsViewModel
{
    public CrosswalkLoadResult? CrosswalkResult { get; set; }
    public WeightsHistoryLoadResult? WeightsHistoryResult { get; set; }
    public BaseRatesLoadResult? PmtacResult { get; set; }
    public BaseRatesLoadResult? DtcResult { get; set; }
    public MasterResetResult? MasterResetResult { get; set; }
    public CmsCacheRefreshResult? CmsRefreshResult { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ErrorContext { get; set; }   // which card the error came from
}
