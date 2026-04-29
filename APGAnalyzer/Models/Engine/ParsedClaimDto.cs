namespace APGAnalyzer.Models.Engine;

/// <summary>
/// One claim entering the APG engine. Mirrors ParsedClaim in the Python
/// service. The Rate Calculator builds a synthetic instance per session;
/// the EDI 837/835 parsers (Phase 4) will populate it from real claim data.
/// </summary>
public class ParsedClaimDto
{
    public string ClaimId { get; set; } = "";
    public DateOnly? DateOfService { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal AllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PrincipalDiagnosis { get; set; }   // already normalized when set
    public List<string> OtherDiagnoses { get; set; } = new();
    public List<ServiceLineDto> ServiceLines { get; set; } = new();
}
