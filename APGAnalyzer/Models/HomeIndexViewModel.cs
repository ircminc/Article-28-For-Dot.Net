namespace APGAnalyzer.Models;

/// <summary>
/// Drives the home page's "is everything wired?" panel. Each property is
/// the row count of one reference table — non-zero means a successful
/// upload (or seed) of that reference dataset.
/// </summary>
public class HomeIndexViewModel
{
    public bool DbConnected { get; set; }
    public string? DbError { get; set; }
    public int HcpcsRows { get; set; }
    public int Icd10Rows { get; set; }
    public int ApgWeightRows { get; set; }
    public int ApgBaseRateRows { get; set; }
    public int ProviderCountyRows { get; set; }
    public int PxBasedWeightRows { get; set; }
    public int FeeScheduleRows { get; set; }
    public int IdentityUserRows { get; set; }

    public int TotalReferenceRows =>
        HcpcsRows + Icd10Rows + ApgWeightRows + ApgBaseRateRows
        + ProviderCountyRows + PxBasedWeightRows + FeeScheduleRows;
}
