using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Maps a HCPCS/CPT procedure code to its EAPG (Enhanced Ambulatory Patient
/// Group) assignment, date-bounded by quarter. Loaded from eMedNY's APG
/// Crosswalk workbook (HCPCS → EAPGs sheet) under the Solventum v3.18
/// taxonomy. The APG engine uses this to assign each line's EAPG.
/// </summary>
[Table("hcpcs_to_eapg")]
public class HcpcsToEapg
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(12)]
    public string Hcpcs { get; set; } = "";

    [MaxLength(512)]
    public string? Description { get; set; }

    public int Eapg { get; set; }

    [MaxLength(512)]
    public string? EapgDesc { get; set; }

    [MaxLength(64)]
    public string? EapgType { get; set; }

    [MaxLength(256)]
    public string? EapgCategory { get; set; }

    [MaxLength(32)]
    public string? EapgServiceLine { get; set; }

    public DateOnly? QuarterEffectiveDate { get; set; }
    public DateOnly? QuarterEndDate { get; set; }
    public DateOnly? MidQuarterEffectiveDate { get; set; }
    public DateOnly? MidQuarterEndDate { get; set; }
}
