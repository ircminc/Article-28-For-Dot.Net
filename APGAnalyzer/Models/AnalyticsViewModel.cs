namespace APGAnalyzer.Models;

/// <summary>
/// Filter inputs for the analytics dashboard. Bound from query string
/// so links + back-button preserve state; all fields optional.
/// </summary>
public class AnalyticsFilters
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? PayerName { get; set; }
    public string? FileType { get; set; }       // 835I | 835P | 837I | 837P
    public string? ProviderNpi { get; set; }

    /// <summary>How to bucket the Compression breakdown.
    /// One of: eapg | procedure | peer_group | region | date_year</summary>
    public string GroupBy { get; set; } = "procedure";

    /// <summary>Trend bucketing: monthly | quarterly</summary>
    public string TrendPeriod { get; set; } = "monthly";

    public bool IsEmpty =>
        DateFrom is null && DateTo is null
        && string.IsNullOrEmpty(PayerName)
        && string.IsNullOrEmpty(FileType)
        && string.IsNullOrEmpty(ProviderNpi);
}

/// <summary>
/// Summary statistics + breakdowns for the Analytics dashboard.
/// All money figures are decimals; the view formats them with C2.
/// Per-user isolation is applied by the controller — analysts see only
/// their own claims, admins/viewers see everything (or scoped via View-as).
/// </summary>
public class AnalyticsViewModel
{
    public AnalyticsFilters Filters { get; set; } = new();

    // -- Tier 1: Summary KPIs --
    public int TotalClaims { get; set; }
    public int ClaimsWithApgResult { get; set; }
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalCorrectApg { get; set; }
    public decimal TotalVariance { get; set; }
    public decimal UnderpaymentTotal { get; set; }   // sum of variance where variance > 0
    public decimal AvgCompressionPct { get; set; }
    public decimal DenialRatePct { get; set; }       // claims with claim_status='4' / total

    // -- Status counts --
    public int Underpaid { get; set; }
    public int Overpaid { get; set; }
    public int Match { get; set; }
    public int Unpriced { get; set; }

    // -- Tier 1: File-type rollup --
    public List<FileTypeStat> ByFileType { get; set; } = new();

    // -- Tier 1: Top underpaid claims --
    public List<TopVarianceRow> TopUnderpaid { get; set; } = new();
    public List<TopVarianceRow> TopOverpaid { get; set; } = new();

    // -- Tier 1: Trends (monthly/quarterly time series) --
    public List<TrendPoint> Trends { get; set; } = new();

    // -- Tier 1: Denials by CARC code --
    public List<DenialRow> Denials { get; set; } = new();
    public decimal TotalAdjustmentsAmount { get; set; }

    // -- Tier 1: Top underpaid procedures (CPT/HCPCS rollup) --
    public List<CompressionRow> TopUnderpaidProcedures { get; set; } = new();

    // -- Tier 2: Compression breakdown (group-by switchable) --
    public List<CompressionRow> Compression { get; set; } = new();

    // -- Tier 2: Payer scorecard --
    public List<PayerScorecardRow> PayerScorecard { get; set; } = new();

    // -- Filter-source dropdowns --
    public List<string> AllPayers { get; set; } = new();
    public List<string> AllProviderNpis { get; set; } = new();

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

/// <summary>One point on the trend time series.</summary>
public class TrendPoint
{
    public string Period { get; set; } = "";   // "2025-01" or "2025-Q1"
    public int Claims { get; set; }
    public decimal Billed { get; set; }
    public decimal Paid { get; set; }
    public decimal Variance { get; set; }
}

/// <summary>One row of the Denials by CARC table.</summary>
public class DenialRow
{
    public string GroupCode { get; set; } = "";   // CO | PR | OA | PI | CR
    public string ReasonCode { get; set; } = "";  // 96, 97, 16, etc.
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PctOfAdjustments { get; set; }  // 0–100
}

/// <summary>One row of the Compression / Top procedures breakdown.</summary>
public class CompressionRow
{
    public string Bucket { get; set; } = "";       // EAPG / CPT / peer group / etc.
    public int Count { get; set; }
    public decimal Expected { get; set; }
    public decimal Paid { get; set; }
    public decimal Variance { get; set; }
    public decimal AvgCompressionPct { get; set; }
}

/// <summary>One row of the Payer Scorecard.</summary>
public class PayerScorecardRow
{
    public string PayerName { get; set; } = "";
    public int Claims { get; set; }
    public decimal Billed { get; set; }
    public decimal Paid { get; set; }
    public decimal PaidPctOfBilled { get; set; }   // 0–100
    public int Denied { get; set; }
    public decimal DenialRatePct { get; set; }
    public int ApgClaims { get; set; }
    public decimal ApgVarianceTotal { get; set; }
    public decimal ApgUnderpaymentTotal { get; set; }
    public decimal ApgAvgCompressionPct { get; set; }
}
