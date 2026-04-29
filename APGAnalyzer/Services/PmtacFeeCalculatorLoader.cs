using System.Diagnostics;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

public interface IPmtacFeeCalculatorLoader
{
    Task<BaseRatesLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default);
}

/// <summary>
/// Loads PMTAC's compiled "Updated APG Fee Calculator" workbook
/// (.xlsx). Reads only the <c>Updated APG Base Rate</c> sheet and
/// replaces every row in <c>apg_base_rates</c> where source='dtc'.
///
/// Layout:
///   row 0:  section title
///   row 1:  blank
///   row 2:  header row (Peer Group | **Base Rate Code |
///           ***Blend Rate Code | Capital Rate Code | Region |
///           [compound key] | [11 date columns from 2009-09-01
///                            through 4/1/2022])
///   row 3+: data
///   end:    footnote rows starting with '*'
///
/// Mirrors backend/db/init_apg_base_rates_v2.py.
/// </summary>
public class PmtacFeeCalculatorLoader(ApplicationDbContext db, ILogger<PmtacFeeCalculatorLoader> log)
    : IPmtacFeeCalculatorLoader
{
    private const string SheetName = "Updated APG Base Rate";
    private const int HeaderRow = 2;
    private const int FirstDateCol = 6;
    private const int DataStartRow = 3;

    public async Task<BaseRatesLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BaseRatesLoadResult { FileName = fileName };

        var sheets = WorkbookReader.ReadAll(fileBytes, fileName);
        var sheet = sheets.FirstOrDefault(s => s.Name == SheetName)
            ?? throw new InvalidOperationException(
                $"Sheet '{SheetName}' not found. Got: {string.Join(", ", sheets.Select(s => s.Name))}");

        if (sheet.Rows.Count <= DataStartRow)
            throw new InvalidOperationException($"'{SheetName}' sheet too short.");

        // Validate header row
        var header = sheet.Rows[HeaderRow];
        if (CellCoerce.CleanString(header.ElementAtOrDefault(0)) != "Peer Group")
            throw new InvalidOperationException(
                $"'{SheetName}' row {HeaderRow + 1} column A: expected 'Peer Group'; got "
              + $"'{CellCoerce.CleanString(header.ElementAtOrDefault(0))}'.");

        // Parse date-column headers
        var dateColumns = new List<(int colIdx, DateOnly effDate)>();
        for (int c = FirstDateCol; c < header.Count; c++)
        {
            var d = CellCoerce.ParseHeaderDate(header[c]);
            if (d.HasValue) dateColumns.Add((c, d.Value));
        }
        if (dateColumns.Count == 0)
            throw new InvalidOperationException($"'{SheetName}': no effective-date columns detected.");
        log.LogInformation("PMTAC: {Count} date columns ({First} → {Last})",
            dateColumns.Count, dateColumns[0].effDate, dateColumns[^1].effDate);

        // Replace existing source='dtc' rows
        result.RowsDeleted = await db.ApgBaseRates
            .Where(x => x.Source == "dtc")
            .ExecuteDeleteAsync(ct);

        var newRows = new List<ApgBaseRate>();
        var peerGroups = new HashSet<string>();
        var datesSeen = new HashSet<DateOnly>();

        for (int r = DataStartRow; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            if (row.Count == 0) continue;

            var peer = CellCoerce.CleanString(row.ElementAtOrDefault(0));
            if (string.IsNullOrEmpty(peer)) break;          // first blank row ends the data block
            if (peer.StartsWith("*")) break;                 // hit footnotes — stop

            var baseRc = CellCoerce.CleanCode(row.ElementAtOrDefault(1));
            var blendRc = CellCoerce.CleanCode(row.ElementAtOrDefault(2));
            var capRc = CellCoerce.CleanCode(row.ElementAtOrDefault(3));
            var region = CellCoerce.CleanString(row.ElementAtOrDefault(4));

            if (string.IsNullOrEmpty(region))
            {
                log.LogWarning("PMTAC row {Row} ({Peer}) has no region; skipping.", r + 1, peer);
                continue;
            }

            peerGroups.Add(peer);

            foreach (var (colIdx, effDate) in dateColumns)
            {
                if (colIdx >= row.Count) continue;
                var rate = CellCoerce.ToDecimal(row[colIdx]);
                if (!rate.HasValue || rate.Value <= 0) continue;
                datesSeen.Add(effDate);
                newRows.Add(new ApgBaseRate
                {
                    Source = "dtc",
                    PeerGroup = peer,
                    BaseRateCode = baseRc,
                    BlendRateCode = blendRc,
                    CapitalRateCode = capRc,
                    Region = region,
                    EffectiveDate = effDate,
                    Rate = rate.Value,
                });
            }
        }

        if (newRows.Count == 0)
            throw new InvalidOperationException($"'{SheetName}': parsed 0 usable rate rows.");

        const int chunkSize = 1_000;
        for (int i = 0; i < newRows.Count; i += chunkSize)
        {
            db.ApgBaseRates.AddRange(newRows.Skip(i).Take(chunkSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        result.RowsInserted = newRows.Count;
        result.DistinctPeerGroups = peerGroups.Count;
        result.DistinctEffectiveDates = datesSeen.Count;
        result.MostRecentEffectiveDate = datesSeen.Count > 0 ? datesSeen.Max() : null;
        result.Elapsed = stopwatch.Elapsed;

        log.LogInformation(
            "PMTAC base rates: {Deleted} deleted, {Inserted} inserted ({Pgs} peer groups × {Dates} dates; newest {Newest}) in {Elapsed:F1}s",
            result.RowsDeleted, result.RowsInserted, result.DistinctPeerGroups,
            result.DistinctEffectiveDates, result.MostRecentEffectiveDate, result.Elapsed.TotalSeconds);

        return result;
    }
}
