namespace APGAnalyzer.Models.Engine;

/// <summary>Claim-level APG calculation output.</summary>
public class APGResult
{
    public string ClaimId { get; set; } = "";
    public DateOnly? DateOfService { get; set; }
    public string PeerGroup { get; set; } = "";
    public string Region { get; set; } = "";
    public decimal BaseRateApplied { get; set; }
    public decimal CorrectApgPayment { get; set; }
    public decimal ActualPaid { get; set; }
    public decimal Variance { get; set; }
    public decimal CompressionPct { get; set; }
    public bool Underpaid { get; set; }
    public bool Overpaid { get; set; }
    public bool DiscountingApplied { get; set; }
    public bool U6Applied { get; set; }
    public bool CapitalApplied { get; set; }
    public decimal CapitalAddonAmount { get; set; }
    public List<APGLineResult> LineDetails { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Informational: what EAPG the principal ICD-10 maps to. Surfaced
/// alongside the per-line HCPCS math on the Rate Calculator so users
/// can verify the system recognizes their diagnosis. The actual claim
/// payment is still driven by per-line HCPCS EAPG assignments.
/// </summary>
public class ICDBasedEAPG
{
    public string DxCode { get; set; } = "";        // normalized
    public string InputDxCode { get; set; } = "";   // exactly what the user typed
    public int? Eapg { get; set; }
    public string? EapgDesc { get; set; }
    public EapgType EapgType { get; set; } = EapgType.Unknown;
    public string? EapgTypeRaw { get; set; }
    public string? EapgCategory { get; set; }
    public decimal? Weight { get; set; }
    public decimal BaseRate { get; set; }
    public decimal? IndicativePayment { get; set; }
    public string? Note { get; set; }
}
