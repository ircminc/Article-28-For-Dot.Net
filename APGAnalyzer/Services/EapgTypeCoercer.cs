using APGAnalyzer.Models.Engine;

namespace APGAnalyzer.Services;

/// <summary>
/// Maps the eMedNY v3.18 crosswalk's rich type strings (25+ categories)
/// down to the engine's canonical 5 types. The packaging /
/// discounting / visit-purpose-override rules only care about these 5.
///
/// Examples:
///   "Radiologic Procedure"            → SignificantProcedure
///   "Diagnostic or Therapeutic Proc"  → SignificantProcedure
///   "Per Diem"                        → MedicalVisit
///   "Drug" / "DME"                    → Ancillary
///   "Incidental"                      → Incidental  (placeholder for E/M codes
///                                                    when no dx is present)
///
/// Mirrors _coerce_eapg_type in the Python service.
/// </summary>
public static class EapgTypeCoercer
{
    private static readonly Dictionary<string, EapgType> Map =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Legacy canonical five
        ["significant procedure"]            = EapgType.SignificantProcedure,
        ["medical visit"]                    = EapgType.MedicalVisit,
        ["ancillary"]                        = EapgType.Ancillary,
        ["incidental"]                       = EapgType.Incidental,
        ["add-on"]                           = EapgType.AddOn,
        ["add on"]                           = EapgType.AddOn,
        // v3.18 expanded taxonomy
        ["per diem"]                         = EapgType.MedicalVisit,
        ["drug"]                             = EapgType.Ancillary,
        ["dme"]                              = EapgType.Ancillary,
        ["unassigned"]                       = EapgType.Unknown,
        ["physical therapy & rehab"]         = EapgType.SignificantProcedure,
        ["physical therapy and rehab"]       = EapgType.SignificantProcedure,
        ["behavioral health & counseling"]   = EapgType.SignificantProcedure,
        ["behavioral health and counseling"] = EapgType.SignificantProcedure,
        ["dental or oral surgery procs"]     = EapgType.SignificantProcedure,
        ["dental or oral surgery"]           = EapgType.SignificantProcedure,
        ["radiologic procedure"]             = EapgType.SignificantProcedure,
        ["diagnostic or therapeutic proc"]   = EapgType.SignificantProcedure,
        ["diagnostic or therapeutic procedure"] = EapgType.SignificantProcedure,
    };

    public static EapgType Coerce(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EapgType.Unknown;
        var key = raw.Trim();
        if (Map.TryGetValue(key, out var t)) return t;
        return EapgType.Unknown;
    }
}
