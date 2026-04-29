using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// APG base rate (dollars per APG-weight unit) by source / peer group /
/// region / effective date. Drives every APG line payment.
///
/// Lookup rule (engine): exact match on source + peer_group + region;
/// effective_date ≤ DOS; pick the most recent.
/// </summary>
[Table("apg_base_rates")]
public class ApgBaseRate
{
    [Key]
    public int Id { get; set; }

    /// <summary>'dtc' (freestanding) or 'hospital'.</summary>
    [Required, MaxLength(16)]
    public string Source { get; set; } = "";

    /// <summary>e.g. 'Clinic*', 'Amb Surg', 'Renal',
    /// 'School-Based Health Center (SBHC)*'</summary>
    [Required, MaxLength(64)]
    public string PeerGroup { get; set; } = "";

    [MaxLength(32)]
    public string? CureCode { get; set; }

    [MaxLength(32)]
    public string? BaseRateCode { get; set; }

    [MaxLength(32)]
    public string? BlendRateCode { get; set; }

    [MaxLength(32)]
    public string? CapitalRateCode { get; set; }

    /// <summary>'Upstate' or 'Downstate'.</summary>
    [Required, MaxLength(16)]
    public string Region { get; set; } = "";

    /// <summary>Hospital-only metadata column.</summary>
    [MaxLength(32)]
    public string? CheatFlag { get; set; }

    public DateOnly EffectiveDate { get; set; }

    [Column(TypeName = "decimal(12,4)")]
    public decimal Rate { get; set; }
}
