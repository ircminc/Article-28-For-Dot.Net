namespace APGAnalyzer.Services;

/// <summary>
/// Canonicalizes ICD-10-CM diagnosis codes for storage and lookup.
///
/// NYS DOH / eMedNY / Solventum publish codes in dot-free canonical form
/// ("I10", "A000", "I4891"). Real-world inputs vary wildly — Rate Calculator
/// users type "I10.0", EDI 837 HI segments may include or omit dots,
/// case is inconsistent. Normalizing at every boundary keeps the
/// engine's lookup from missing because of formatting drift.
///
/// Mirrors normalize_dx_code() in the Python service exactly.
/// </summary>
public static class DxCodeNormalizer
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().ToUpperInvariant().Replace(".", "");
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
