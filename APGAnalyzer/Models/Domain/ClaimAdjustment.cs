using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// CAS segment — both claim-level (LineSeq null) and line-level.
/// Mirrors claim_adjustment in the Python service.
/// </summary>
[Table("claim_adjustment")]
public class ClaimAdjustment
{
    [Key]
    public int Id { get; set; }

    public int ClaimIdFk { get; set; }
    [ForeignKey(nameof(ClaimIdFk))]
    public ParsedClaim Claim { get; set; } = null!;

    /// <summary>null = claim-level adjustment; non-null = line-level.</summary>
    public int? LineSeq { get; set; }

    [Required, MaxLength(4)]
    public string GroupCode { get; set; } = "";       // CO, PR, OA, PI, CR

    [Required, MaxLength(8)]
    public string ReasonCode { get; set; } = "";      // CARC

    [Column(TypeName = "decimal(14,2)")]
    public decimal Amount { get; set; }

    public int? Quantity { get; set; }
}
