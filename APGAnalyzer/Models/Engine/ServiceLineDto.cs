namespace APGAnalyzer.Models.Engine;

/// <summary>
/// One service line entering the APG engine. Mirrors the Python
/// ServiceLine pydantic schema. Used by the Rate Calculator and (later)
/// the EDI 837/835 parsers.
/// </summary>
public class ServiceLineDto
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
}
