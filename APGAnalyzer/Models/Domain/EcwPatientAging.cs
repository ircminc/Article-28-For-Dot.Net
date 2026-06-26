using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW report 31.08 — Patient Balance Aging Report Detail.
[Table("ecw_patient_aging")]
public class EcwPatientAging
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    [MaxLength(200)] public string? PatientName   { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }
    public DateOnly? PatientDob { get; set; }

    [MaxLength(50)]  public string? ClaimNo     { get; set; }
    public DateOnly? ClaimDate   { get; set; }
    public DateOnly? ServiceDate { get; set; }

    public decimal ClaimAmount  { get; set; }
    public decimal Balance      { get; set; }

    // 30-day aging buckets (dollar amounts)
    public decimal Days0To30    { get; set; }
    public decimal Days31To60   { get; set; }
    public decimal Days61To90   { get; set; }
    public decimal Days91To120  { get; set; }
    public decimal Days121To150 { get; set; }
    public decimal Days151To180 { get; set; }
    public decimal DaysOver180  { get; set; }

    public int NoOfStatementsSent { get; set; }
}
