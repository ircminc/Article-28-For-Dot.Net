namespace APGAnalyzer.Services;

/// <summary>
/// Summary returned by the eMedNY APG Crosswalk loader. Surfaced to the
/// admin UI so the user can confirm the upload landed (e.g. "21,000 HCPCS
/// rows + 75,000 ICD-10 rows replaced the previous data").
/// </summary>
public class CrosswalkLoadResult
{
    public int HcpcsRows { get; set; }
    public int Icd10Rows { get; set; }
    public int EapgTypeMappings { get; set; }
    public int HcpcsRowsDeleted { get; set; }
    public int Icd10RowsDeleted { get; set; }
    public string? FileName { get; set; }
    public TimeSpan Elapsed { get; set; }
}
