using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Flat-rate fee for specific HCPCS codes. When present and non-zero,
/// the line pays reimbursement × min(units, max_units) and the APG
/// formula is BYPASSED entirely. (Priority #1 in the pricing ladder.)
/// Loaded from NYS DOH history_and_fee_schedule.xls, "Fee Schedule".
/// </summary>
[Table("fee_schedule")]
public class FeeScheduleItem
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(12)]
    public string Hcpcs { get; set; } = "";

    [MaxLength(256)]
    public string? Description { get; set; }

    public DateOnly EffectiveDate { get; set; }

    /// <summary>Dollars (flat rate per unit).</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal Reimbursement { get; set; }

    /// <summary>Caps billed units to this value when computing payment.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? MaxUnits { get; set; }
}
