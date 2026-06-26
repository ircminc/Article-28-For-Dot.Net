using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW report 361.05 — Financial Analysis at Claim Level.
[Table("ecw_claim_financial")]
public class EcwClaimFinancial
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    [MaxLength(50)]  public string? ClaimNo           { get; set; }
    public DateOnly? ServiceDate  { get; set; }
    public DateOnly? ClaimDate    { get; set; }

    [MaxLength(20)]  public string? ClaimStatusCode      { get; set; }
    [MaxLength(100)] public string? ClaimStatusGroupName { get; set; }

    [MaxLength(200)] public string? PrimaryPayer    { get; set; }
    [MaxLength(200)] public string? SecondaryPayer  { get; set; }
    [MaxLength(200)] public string? TertiaryPayer   { get; set; }
    [MaxLength(200)] public string? Facility        { get; set; }
    [MaxLength(10)]  public string? FacilityPos     { get; set; }

    [MaxLength(200)] public string? AppointmentProvider { get; set; }
    [MaxLength(200)] public string? RenderingProvider   { get; set; }

    [MaxLength(200)] public string? Patient      { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }
    [MaxLength(10)]  public string? PatientGender { get; set; }
    public int?      PatientAge  { get; set; }

    [MaxLength(10)]  public string? VisitType { get; set; }
    public bool ClaimVoided { get; set; }

    public decimal BilledCharge          { get; set; }
    public decimal PayerCharge           { get; set; }
    public decimal SelfCharge            { get; set; }
    public decimal Payments              { get; set; }
    public decimal PayerPayment          { get; set; }
    public decimal PatientPayment        { get; set; }
    public decimal ContractualAdjustment { get; set; }
    public decimal PayerWithheld         { get; set; }
    public decimal WriteoffAdjustment    { get; set; }
    public decimal Refund                { get; set; }
    public decimal Balance               { get; set; }
}
