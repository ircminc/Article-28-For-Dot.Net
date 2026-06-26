using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW report 13.10 — Progress Note Completion Date vs Claim Created Date.
[Table("ecw_billing_lag")]
public class EcwBillingLag
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    [MaxLength(50)]  public string? EncounterId  { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }
    [MaxLength(200)] public string? PatientName   { get; set; }
    [MaxLength(200)] public string? Provider      { get; set; }
    [MaxLength(100)] public string? VisitType     { get; set; }

    public DateOnly? AppointmentDate          { get; set; }
    public DateOnly? ProgressNoteLastLockedOn { get; set; }

    [MaxLength(50)] public string? ChartLockStatus { get; set; }
    public int? DaysApptToLocked      { get; set; }

    [MaxLength(50)] public string? ClaimNo   { get; set; }
    public DateOnly? ClaimDate               { get; set; }
    public int? DaysPnToClaimCreated         { get; set; }

    [MaxLength(300)] public string? WorkflowStatus { get; set; }
}
