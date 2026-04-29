namespace APGAnalyzer.Services.Edi;

/// <summary>
/// Top-level result of parsing one 835I file. Mirrors Parsed835I in the
/// Python service. The list of <see cref="Claims"/> is the main payload —
/// the envelope-level fields (payer, payee, payment trace) are kept for
/// audit / display but rarely drive logic.
/// </summary>
public class Parsed835IResult
{
    public string InterchangeSender { get; set; } = "";
    public string InterchangeReceiver { get; set; } = "";
    public DateOnly? InterchangeDate { get; set; }
    public string TransactionSetId { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public decimal PaymentAmount { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public string CheckEftTrace { get; set; } = "";
    public string PayerName { get; set; } = "";
    public string PayerId { get; set; } = "";
    public string PayeeName { get; set; } = "";
    public string PayeeNpi { get; set; } = "";
    public List<ParsedClaimDto> Claims { get; set; } = new();
}

/// <summary>
/// Parser-emitted shape of one CLP loop. Independent of the engine's
/// own ParsedClaimDto in Models/Engine — this carries 835/837-specific
/// fields like patient_id, payer info, and adjustment lists. The
/// upload pipeline converts to engine-DTO at calculation time.
/// </summary>
public class ParsedClaimDto
{
    public string FileType { get; set; } = "835I";
    public string ClaimId { get; set; } = "";
    public string? ClaimStatus { get; set; }
    public string? ClaimFilingIndicator { get; set; }
    public string? PayerName { get; set; }
    public string? PayerId { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderNpi { get; set; }
    public string? PatientName { get; set; }
    public string? PatientId { get; set; }
    public DateOnly? DateOfService { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal AllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PatientResponsibility { get; set; }
    public string? PrincipalDiagnosis { get; set; }
    public List<string> OtherDiagnoses { get; set; } = new();
    public List<ParsedServiceLineDto> ServiceLines { get; set; } = new();
    public List<ParsedAdjustmentDto> Adjustments { get; set; } = new();
}

public class ParsedServiceLineDto
{
    public int LineSeq { get; set; }
    public string ProcedureCode { get; set; } = "";
    public List<string> Modifiers { get; set; } = new();
    public string? RevenueCode { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal AllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public int Units { get; set; } = 1;
    public DateOnly? DateOfService { get; set; }
    public List<ParsedAdjustmentDto> Adjustments { get; set; } = new();
}

public class ParsedAdjustmentDto
{
    public int? LineSeq { get; set; }   // null = claim-level
    public string GroupCode { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public decimal Amount { get; set; }
    public int? Quantity { get; set; }
}
