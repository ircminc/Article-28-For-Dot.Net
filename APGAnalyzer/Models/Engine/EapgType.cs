namespace APGAnalyzer.Models.Engine;

/// <summary>
/// Canonical EAPG types the APG engine reasons about. The eMedNY v3.18
/// crosswalk publishes 25+ richer category names — they're all collapsed
/// to one of these five via EapgTypeCoercer at lookup time, since
/// packaging / discounting / visit-purpose-override rules only care
/// about the canonical category.
///
/// Mirrors backend/models/schemas.py:EapgType in the Python service.
/// </summary>
public enum EapgType
{
    Unknown,
    SignificantProcedure,
    MedicalVisit,
    Ancillary,
    Incidental,
    AddOn,
}
