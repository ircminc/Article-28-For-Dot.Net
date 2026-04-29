namespace APGAnalyzer.Models;

/// <summary>
/// Summary statistics + breakdowns for the Analytics dashboard.
/// All money figures are decimals to keep precision; the view formats
/// them with C2.
/// </summary>
public class AnalyticsViewModel
{
    // -- Top-line summary (across all claims) --
    public int TotalClaims { get; set; }
    public int ClaimsWithApgResult { get; set; }
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalCorrectApg { get; set; }
    public decimal TotalVariance { get; set; }

    // -- Status counts --
    public int Underpaid { get; set; }
    public int Overpaid { get; set; }
    public int Match { get; set; }
    public int Unpriced { get; set; }

    // -- File-type rollup --
    public List<FileTypeStat> ByFileType { get; set; } = new();

    // -- Top 10 underpaid claims (drives the "biggest variance" table) --
    public List<TopVarianceRow> TopUnderpaid { get; set; } = new();

    // -- Top 10 overpaid (smaller list, often empty) --
    public List<TopVarianceRow> TopOverpaid { get; set; } = new();

    public bool IsEmpty => TotalClaims == 0;
}

public class FileTypeStat
{
    public string FileType { get; set; } = "";
    public int Count { get; set; }
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalCorrectApg { get; set; }
    public decimal TotalVariance { get; set; }
}

public class TopVarianceRow
{
    public int Id { get; set; }
    public string ClaimId { get; set; } = "";
    public string FileType { get; set; } = "";
    public string? PatientName { get; set; }
    public DateOnly? DateOfService { get; set; }
    public decimal CorrectApg { get; set; }
    public decimal Paid { get; set; }
    public decimal Variance { get; set; }
}
