using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// Parsed from eCW report 371.05 — Financial Analysis at CPT Level.
[Table("ecw_cpt_line")]
public class EcwCptLine
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public EcwAuditBatch? Batch { get; set; }

    [MaxLength(50)]  public string? ClaimNo      { get; set; }
    [MaxLength(50)]  public string? PatientAcctNo { get; set; }
    [MaxLength(200)] public string? Patient       { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public DateOnly? ClaimDate   { get; set; }

    [MaxLength(200)] public string? PrimaryPayer { get; set; }
    [MaxLength(200)] public string? Facility     { get; set; }
    [MaxLength(10)]  public string? FacilityPos  { get; set; }
    [MaxLength(200)] public string? RenderingProvider { get; set; }

    [MaxLength(20)]  public string? CptCode        { get; set; }
    [MaxLength(300)] public string? CptDescription { get; set; }
    [MaxLength(100)] public string? CptGroupName   { get; set; }

    [MaxLength(10)] public string? Modifier1 { get; set; }
    [MaxLength(10)] public string? Modifier2 { get; set; }
    [MaxLength(10)] public string? Modifier3 { get; set; }
    [MaxLength(10)] public string? Modifier4 { get; set; }

    [MaxLength(20)]  public string? Icd1Code { get; set; }
    [MaxLength(300)] public string? Icd1Name { get; set; }
    [MaxLength(20)]  public string? Icd2Code { get; set; }
    [MaxLength(20)]  public string? Icd3Code { get; set; }
    [MaxLength(20)]  public string? Icd4Code { get; set; }

    public decimal BilledCharge          { get; set; }
    public decimal TotalPayment          { get; set; }
    public decimal PayerPayment          { get; set; }
    public decimal PatientPayment        { get; set; }
    public decimal ContractualAdjustment { get; set; }
    public decimal WriteoffAdjustment    { get; set; }
    public decimal Balance               { get; set; }
    public decimal FeeScheduleAllowedFee { get; set; }
    public int     BilledUnits           { get; set; }
    public bool    IsTelevisit           { get; set; }
}
