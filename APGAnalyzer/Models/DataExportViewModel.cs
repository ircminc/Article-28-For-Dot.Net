namespace APGAnalyzer.Models;

/// <summary>
/// Drives the "Full data export" configuration page. The user picks
/// which entity tables to include (sheets in the resulting workbook)
/// and, if scoping to one claim, the claim ID.
/// </summary>
public class DataExportViewModel
{
    // -- Sheet selection --
    public bool IncludeClaims         { get; set; } = true;
    public bool IncludeServiceLines   { get; set; } = true;
    public bool IncludeAdjustments    { get; set; } = true;
    public bool IncludeApgResults     { get; set; } = true;
    public bool IncludeApgLineDetails { get; set; } = true;

    // -- Scope --
    /// <summary>If set, the export covers only this single claim.</summary>
    public int? ClaimId { get; set; }

    /// <summary>For bulk mode: filter inputs (mirror Claims list filters).</summary>
    public ClaimsListFilters Filters { get; set; } = new();

    /// <summary>
    /// If non-empty, the export is scoped to exactly these claim IDs (selected
    /// from the Claims list checkboxes). Takes precedence over <see cref="Filters"/>.
    /// </summary>
    public List<int> SelectedIds { get; set; } = new();

    // -- Display helpers --
    public string Mode => ClaimId.HasValue
        ? "single"
        : (SelectedIds.Count > 0 ? "selected" : "bulk");

    public bool AnySheetSelected =>
        IncludeClaims || IncludeServiceLines || IncludeAdjustments
        || IncludeApgResults || IncludeApgLineDetails;
}
