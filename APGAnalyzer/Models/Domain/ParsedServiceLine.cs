using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// One row per SVC/SV1 line on a ParsedClaim.
/// Mirrors parsed_service_line in the Python service.
/// </summary>
[Table("parsed_service_line")]
public class ParsedServiceLine
{
    [Key]
    public int Id { get; set; }

    public int ClaimIdFk { get; set; }
    [ForeignKey(nameof(ClaimIdFk))]
    public ParsedClaim Claim { get; set; } = null!;

    public int LineSeq { get; set; }

    [Required, MaxLength(12)]
    public string ProcedureCode { get; set; } = "";

    /// <summary>JSON list[str] of modifiers.</summary>
    public string? ModifiersJson { get; set; }

    [MaxLength(8)]
    public string? RevenueCode { get; set; }

    [Column(TypeName = "decimal(14,2)")] public decimal BilledAmount { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal AllowedAmount { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal PaidAmount { get; set; }
    public int Units { get; set; } = 1;
    public DateOnly? DateOfService { get; set; }
}
