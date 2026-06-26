using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

[Table("ecw_audit_batch")]
public class EcwAuditBatch
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string PracticeName { get; set; } = "";

    public DateOnly AuditDate { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Navigation
    public ICollection<EcwClaimFinancial> ClaimFinancials { get; set; } = new List<EcwClaimFinancial>();
    public ICollection<EcwCptLine>        CptLines        { get; set; } = new List<EcwCptLine>();
    public ICollection<EcwSubmission>     Submissions     { get; set; } = new List<EcwSubmission>();
    public ICollection<EcwBillingLag>     BillingLags     { get; set; } = new List<EcwBillingLag>();
    public ICollection<EcwPatientAging>   PatientAgings   { get; set; } = new List<EcwPatientAging>();
    public ICollection<EcwPayerAging>     PayerAgings     { get; set; } = new List<EcwPayerAging>();
}
