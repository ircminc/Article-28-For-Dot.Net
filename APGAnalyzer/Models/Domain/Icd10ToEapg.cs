using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Maps an ICD-10-CM diagnosis code to its EAPG assignment, date-bounded.
/// Loaded from eMedNY's APG Crosswalk workbook (ICD-10 DX → EAPGs sheet).
///
/// Storage convention: <see cref="DxCode"/> is normalized — uppercased
/// with dots stripped (e.g. "I10.0" → "I100", "i48.91" → "I4891"). The
/// Solventum source already publishes in this canonical form. Applying
/// the same normalizer at ingest + lookup time means user input formats
/// (with or without dots, mixed case) all resolve correctly.
/// </summary>
[Table("icd10_to_eapg")]
public class Icd10ToEapg
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(12)]
    public string DxCode { get; set; } = "";

    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>"0" (any), "M", or "F" from the source.</summary>
    [MaxLength(4)]
    public string? Gender { get; set; }

    public int Eapg { get; set; }

    [MaxLength(512)]
    public string? EapgDesc { get; set; }

    [MaxLength(64)]
    public string? EapgType { get; set; }

    [MaxLength(256)]
    public string? EapgCategory { get; set; }

    [MaxLength(32)]
    public string? EapgServiceLine { get; set; }

    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? EndDate { get; set; }
}
