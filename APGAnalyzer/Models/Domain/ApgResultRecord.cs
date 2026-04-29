using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Cached APG calculation for a parsed claim. Refreshed on reprocess.
/// 1:1 with <see cref="ParsedClaim"/> (PK is the FK to the claim).
/// </summary>
[Table("apg_result")]
public class ApgResultRecord
{
    [Key]
    public int ClaimIdFk { get; set; }
    [ForeignKey(nameof(ClaimIdFk))]
    public ParsedClaim Claim { get; set; } = null!;

    [Column(TypeName = "decimal(14,2)")] public decimal CorrectApgPayment { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal ActualPaid { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal Variance { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal CompressionPct { get; set; }
    public bool Underpaid { get; set; }
    public bool Overpaid { get; set; }

    [Column(TypeName = "decimal(12,4)")] public decimal BaseRateApplied { get; set; }
    [Required, MaxLength(64)] public string PeerGroup { get; set; } = "";
    [Required, MaxLength(16)] public string Region { get; set; } = "";

    public bool DiscountingApplied { get; set; }
    public bool U6Applied { get; set; }
    public bool CapitalApplied { get; set; }

    /// <summary>JSON-serialized list of APGLineResult objects (engine output).</summary>
    public string LineDetailsJson { get; set; } = "[]";

    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
