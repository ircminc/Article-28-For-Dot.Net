using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW report 123.06 — Claim Submission Report.
/// One row per submission event; multiple rows per claim if resubmitted.
[Table("ecw_submission")]
public class EcwSubmission
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    [MaxLength(50)]  public string? ClaimNo      { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }
    [MaxLength(200)] public string? PatientName   { get; set; }

    public DateOnly? ServiceDate  { get; set; }
    public DateOnly? ClaimDate    { get; set; }

    [MaxLength(50)]  public string?  SubmissionType              { get; set; }
    public DateOnly? SubmissionDate                              { get; set; }
    public DateOnly? ClaimFirstSubmissionDate                    { get; set; }
    public DateOnly? ClaimLastSubmissionDate                     { get; set; }

    [MaxLength(200)] public string? PayerName      { get; set; }
    public int     SubmissionCount { get; set; }
    public decimal Charges         { get; set; }

    [MaxLength(500)] public string? LogMessage { get; set; }
}
