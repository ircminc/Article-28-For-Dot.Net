using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// HCPCS-specific weight OVERRIDE. When a procedure has a non-zero row
/// here for the given DOS, this weight replaces the apg_weights lookup.
/// (Priority #2 in the engine's pricing ladder.)
/// Loaded from NYS DOH history_and_fee_schedule.xls, "Final Px Based
/// Weights" sheet.
/// </summary>
[Table("px_based_weights")]
public class PxBasedWeight
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(12)]
    public string Hcpcs { get; set; } = "";

    [MaxLength(256)]
    public string? Description { get; set; }

    public DateOnly EffectiveDate { get; set; }

    [Column(TypeName = "decimal(12,6)")]
    public decimal Weight { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? UnitsLimit { get; set; }
}
