using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

/// <summary>
/// Loads NY county → Upstate/Downstate region mapping from the PMTAC
/// "Updated APG Fee Calculator" workbook's "Provider County" sheet
/// (~62 rows). Replace-all semantics.
///
/// Sheet layout:
///   row 0: header (Provider County Description | Provider County Code |
///                  Health Home Phase | Region)
///   row 1+: data
/// </summary>
public class ProviderCountyLoader(ApplicationDbContext db, ILogger<ProviderCountyLoader> log)
{
    public async Task<int> LoadFromSheetAsync(SheetData sheet, CancellationToken ct)
    {
        if (sheet.Rows.Count < 2) return 0;

        // The header is in row 0; data starts row 1
        var header = sheet.Rows[0];
        if (CellCoerce.CleanString(header.ElementAtOrDefault(0))?.ToLowerInvariant()
            is not "provider county description")
        {
            log.LogWarning(
                "Provider County sheet: unexpected header in column A. Got '{First}'.",
                CellCoerce.CleanString(header.ElementAtOrDefault(0)) ?? "(blank)");
            return 0;
        }

        await db.ProviderCounties.ExecuteDeleteAsync(ct);

        var rows = new List<ProviderCounty>();
        for (int r = 1; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            var name = CellCoerce.CleanString(row.ElementAtOrDefault(0));
            var code = CellCoerce.ToInt(row.ElementAtOrDefault(1));
            var phase = CellCoerce.CleanString(row.ElementAtOrDefault(2));
            var region = CellCoerce.CleanString(row.ElementAtOrDefault(3));
            if (string.IsNullOrEmpty(name) || !code.HasValue || string.IsNullOrEmpty(region))
                continue;
            rows.Add(new ProviderCounty
            {
                CountyCode = code.Value,
                CountyName = name,
                HealthHomePhase = phase,
                Region = region,
            });
        }

        if (rows.Count > 0)
        {
            db.ProviderCounties.AddRange(rows);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        log.LogInformation("Provider counties: {Count} rows loaded", rows.Count);
        return rows.Count;
    }
}
