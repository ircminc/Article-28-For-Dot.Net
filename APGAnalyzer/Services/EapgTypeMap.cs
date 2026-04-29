namespace APGAnalyzer.Services;

/// <summary>
/// Maps numeric EAPG type codes (e.g. "5") to their human-readable v3.18
/// names (e.g. "Incidental"). The eMedNY APG Crosswalk's "EAPG Types"
/// sheet is the authoritative source — it's read at upload time. This
/// fallback dictionary is a safety net so the loader still works if that
/// sheet is missing or has an unexpected layout.
///
/// Source: Solventum EAPG v3.18 type list (April 2026 publication).
/// </summary>
public static class EapgTypeMap
{
    public static readonly IReadOnlyDictionary<string, string> Fallback =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Original five
            ["1"]  = "Per Diem",
            ["2"]  = "Significant Procedure",
            ["3"]  = "Medical Visit",
            ["4"]  = "Ancillary",
            ["5"]  = "Incidental",
            ["6"]  = "Drug",
            ["7"]  = "DME",
            ["8"]  = "Unassigned",
            // v3.18 expanded taxonomy
            ["21"] = "Physical Therapy & Rehab",
            ["22"] = "Behavioral Health & Counseling",
            ["23"] = "Dental or Oral Surgery Procs",
            ["24"] = "Radiologic Procedure",
            ["25"] = "Diagnostic or Therapeutic Proc",
        };

    /// <summary>Lookup with fallback. Returns the input unchanged if no mapping.</summary>
    public static string? Resolve(string? raw, IReadOnlyDictionary<string, string>? sheetMap = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var key = raw.Trim();
        if (sheetMap is not null && sheetMap.TryGetValue(key, out var name)) return name;
        if (Fallback.TryGetValue(key, out var fallback)) return fallback;
        return key;  // unknown code — keep the raw value rather than dropping it
    }
}
