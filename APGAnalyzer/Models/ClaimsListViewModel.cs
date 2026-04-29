using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;

namespace APGAnalyzer.Models;

public class ClaimsListViewModel
{
    public List<ClaimsListRow> Rows { get; set; } = new();
    public int TotalClaims { get; set; }
}

/// <summary>
/// Slim row shape — what the list page needs without loading every
/// service line / adjustment for every row.
/// </summary>
public class ClaimsListRow
{
    public int Id { get; set; }
    public string ClaimId { get; set; } = "";
    public string FileType { get; set; } = "";
    public DateOnly? DateOfService { get; set; }
    public string? PayerName { get; set; }
    public string? PatientName { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? CorrectApgPayment { get; set; }
    public decimal? Variance { get; set; }
    public bool? Underpaid { get; set; }
    public bool? Overpaid { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ClaimDetailViewModel
{
    public ParsedClaim Claim { get; set; } = null!;
    public ApgResultRecord? ApgResult { get; set; }
    public List<APGLineResult> LineDetails { get; set; } = new();
    public List<string> OtherDiagnoses { get; set; } = new();
    public ICDBasedEAPG? IcdBasedResult { get; set; }
}
