namespace APGAnalyzer.Services.EcwAudit;

public enum AuditStatus { Pass, Warn, Fail, Info }

public class AuditCheckResult
{
    public int    CheckId    { get; init; }
    public string CheckName  { get; init; } = "";
    public string Source     { get; init; } = "";
    public string Formula    { get; init; } = "";
    public string Benchmark  { get; init; } = "";

    public AuditStatus Status   { get; init; }
    public string      Score    { get; init; } = ""; // formatted string, e.g. "94.3%"
    public string      Summary  { get; init; } = "";

    // Detail rows: list of (label, value) pairs for the drilldown table
    public List<AuditDetailRow> DetailRows { get; init; } = new();

    // Optional: flag rows (the specific claims that caused a Warn/Fail)
    public List<AuditFlagRow> FlagRows { get; init; } = new();
}

public record AuditDetailRow(string Label, string Value);

public class AuditFlagRow
{
    public string? ClaimNo      { get; init; }
    public string? Patient      { get; init; }
    public string? ServiceDate  { get; init; }
    public string? Payer        { get; init; }
    public string? FlagDetail   { get; init; }
    public decimal? Amount      { get; init; }
}
