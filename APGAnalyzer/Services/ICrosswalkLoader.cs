namespace APGAnalyzer.Services;

public interface ICrosswalkLoader
{
    /// <summary>
    /// Parse the eMedNY APG Crosswalk workbook and replace every row in
    /// hcpcs_to_eapg + icd10_to_eapg with what's in the file.
    /// </summary>
    Task<CrosswalkLoadResult> LoadFromBytesAsync(
        byte[] fileBytes,
        string fileName,
        CancellationToken ct = default);
}
