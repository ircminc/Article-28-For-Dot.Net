using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW reports 31.09 Primary and 31.09 Secondary — Payer Claim Aging.
[Table("ecw_payer_aging")]
public class EcwPayerAging
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    /// true = primary payer row, false = secondary payer row
    public bool IsPrimary { get; set; }

    [MaxLength(200)] public string? PayerName    { get; set; }
    [MaxLength(200)] public string? PatientName  { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }

    public int      AgingDays   { get; set; }
    public DateOnly? ClaimDate   { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public DateOnly? ClaimFirstSubmittedDate { get; set; }
    public DateOnly? LastSubmissionDate      { get; set; }

    [MaxLength(50)] public string? ClaimNo { get; set; }

    public decimal Charges       { get; set; }
    public decimal DaysCurrent   { get; set; }  // 0-30
    public decimal Days31To60    { get; set; }
    public decimal Days61To90    { get; set; }
    public decimal Days91To120   { get; set; }
    public decimal DaysOver120   { get; set; }
    public decimal Balance        { get; set; }
}
