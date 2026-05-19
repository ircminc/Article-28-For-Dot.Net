using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;

namespace APGAnalyzer.Models;

/// <summary>
/// Filter inputs for the claims list. Bound from query string so links
/// + browser back-button preserve state.
/// </summary>
public class ClaimsListFilters
{
    public string? FileType { get; set; }   // 835I | 835P | 837I | 837P | "" = all
    public string? Status { get; set; }     // underpaid | overpaid | match | unpriced | "" = all
    public string? Search { get; set; }     // claim id / patient name / payer
    public DateOnly? DosFrom { get; set; }
    public DateOnly? DosTo { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public bool IsEmpty =>
        string.IsNullOrEmpty(FileType) &&
        string.IsNullOrEmpty(Status) &&
        string.IsNullOrEmpty(Search) &&
        DosFrom is null && DosTo is null;
}

public class ClaimsListViewModel
{
    public List<ClaimsListRow> Rows { get; set; } = new();
    public int TotalClaims { get; set; }            // matching the filter
    public int TotalUnfiltered { get; set; }        // all rows in DB
    public ClaimsListFilters Filters { get; set; } = new();

    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalClaims / Filters.PageSize));
    public bool HasPrev => Filters.Page > 1;
    public bool HasNext => Filters.Page < TotalPages;
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
    public bool IsLinked { get; set; }      // has a sibling 837/835?
    public DateTime CreatedAt { get; set; }
    public string? OwnerUserId { get; set; }    // null = legacy / pre-isolation
    public string? OwnerEmail { get; set; }     // hydrated by the controller
}

public class ClaimDetailViewModel
{
    public ParsedClaim Claim { get; set; } = null!;
    public ApgResultRecord? ApgResult { get; set; }
    public List<APGLineResult> LineDetails { get; set; } = new();
    public List<string> OtherDiagnoses { get; set; } = new();
    public ICDBasedEAPG? IcdBasedResult { get; set; }
    public ParsedClaim? LinkedClaim { get; set; }   // sibling 837/835

    /// <summary>CMS Medicare comparison for professional claims (837P/835P only).
    /// Null when not applicable (institutional claim) or when no locality is configured.</summary>
    public CmsCalculatorResult? CmsResult { get; set; }
}
