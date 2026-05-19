using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using APGAnalyzer.Services;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// One row per CLP segment from a parsed 835/837 file. Header-level claim
/// data; service lines + CAS adjustments are children, the cached APG
/// calculation is a 1:1 child via <see cref="ApgResultRecord"/>.
///
/// Mirrors backend/db/database.py:ParsedClaim in the Python service.
/// </summary>
[Table("parsed_claim")]
public class ParsedClaim : IOwnedByUser
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string FileId { get; set; } = "";          // upload batch id

    [Required, MaxLength(8)]
    public string FileType { get; set; } = "";        // '835I' | '835P' | '837I' | '837P'

    [MaxLength(128)]
    public string? PayerName { get; set; }
    [MaxLength(32)]
    public string? PayerId { get; set; }
    [MaxLength(16)]
    public string? ProviderNpi { get; set; }
    [MaxLength(128)]
    public string? ProviderName { get; set; }

    [Required, MaxLength(64)]
    public string ClaimId { get; set; } = "";         // CLP01

    [MaxLength(128)]
    public string? PatientName { get; set; }
    [MaxLength(64)]
    public string? PatientId { get; set; }

    public DateOnly? DateOfService { get; set; }

    [MaxLength(8)]
    public string? ClaimStatus { get; set; }          // CLP02

    [Column(TypeName = "decimal(14,2)")] public decimal BilledAmount { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal AllowedAmount { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal PaidAmount { get; set; }
    [Column(TypeName = "decimal(14,2)")] public decimal PatientResponsibility { get; set; }

    [MaxLength(4)]
    public string? ClaimFilingIndicator { get; set; } // CLP09

    [MaxLength(16)]
    public string? PrincipalDiagnosis { get; set; }   // normalized — uppercase, no dots

    /// <summary>JSON-serialized list of secondary diagnosis codes.</summary>
    public string? OtherDiagnosesJson { get; set; }

    /// <summary>Self-reference: links an 835 to its 837 sibling (or vice versa).
    /// SET NULL on delete to avoid cascade chains.</summary>
    public int? LinkedClaimIdFk { get; set; }
    [ForeignKey(nameof(LinkedClaimIdFk))]
    public ParsedClaim? LinkedClaim { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// AspNetUsers.Id of the user who uploaded this claim. Drives the
    /// per-user isolation introduced post-go-live: analysts only see
    /// their own claims, admins/viewers see everything (or scope to a
    /// single user via the navbar "View as" dropdown). Child rows
    /// (ServiceLines, Adjustments, ApgResult) inherit ownership through
    /// this column — no separate FK on them.
    /// </summary>
    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    // Children (cascade delete; lazy by default — use Include() when needed)
    public List<ParsedServiceLine> ServiceLines { get; set; } = new();
    public List<ClaimAdjustment> Adjustments { get; set; } = new();
    public ApgResultRecord? ApgResult { get; set; }
}
