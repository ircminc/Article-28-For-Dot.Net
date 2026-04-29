namespace APGAnalyzer.Models.Engine;

/// <summary>
/// Per-line APG calculation output. The Rate Calculator surfaces every
/// field — the engine's notes (e.g. "Visit-purpose adjustment: HCPCS 99213
/// maps to Incidental placeholder EAPG 491; using ICD-10 E119's EAPG 713
/// instead.") explain how each number was derived.
/// </summary>
public class APGLineResult
{
    public int LineSeq { get; set; }
    public string ProcedureCode { get; set; } = "";
    public List<string> Modifiers { get; set; } = new();
    public int? Eapg { get; set; }
    public string? EapgDesc { get; set; }
    public EapgType EapgType { get; set; } = EapgType.Unknown;
    public string? EapgTypeRaw { get; set; }       // the v3.18 string before coercion
    public string? EapgCategory { get; set; }
    public decimal? Weight { get; set; }
    public decimal BaseRate { get; set; }
    public decimal ExpectedPayment { get; set; }
    public decimal ActualPaid { get; set; }
    public decimal Variance { get; set; }
    public bool Packaged { get; set; }
    public bool Discounted { get; set; }
    public bool U6Applied { get; set; }
    public bool Denied { get; set; }
    public bool FeeScheduled { get; set; }
    public bool PxWeightApplied { get; set; }
    public List<string> Notes { get; set; } = new();
}
