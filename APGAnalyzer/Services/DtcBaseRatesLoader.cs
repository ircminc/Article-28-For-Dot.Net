using System.Diagnostics;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

public interface IDtcBaseRatesLoader
{
    Task<BaseRatesLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default);
}

/// <summary>
/// Loads NYS DOH's Freestanding APG base-rate inventory file
/// (<c>dtc_base_rates_inv.xls</c> or .xlsx). Replaces every row in
/// <c>apg_base_rates</c> where source='dtc'; leaves hospital base rates,
/// crosswalks, weights, and operational data untouched.
///
/// Layout (verified against July-2022 inventory):
///   row 0:    title (ignored)
///   row 1:    blank
///   row 2:    column headers (Peer Group | **Base Rate Code |
///             ***Blend Rate Code | Capital Rate Code | Region |
///             N date columns)
///   row 3+:   data rows (one per peer_group × region)
///   final:    footnote rows, ignored
///
/// Tolerates extra/missing/reordered columns and trailing footer text.
/// Mirrors backend/db/init_dtc_rates.py.
/// </summary>
public class DtcBaseRatesLoader(ApplicationDbContext db, ILogger<DtcBaseRatesLoader> log)
    : IDtcBaseRatesLoader
{
    public async Task<BaseRatesLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BaseRatesLoadResult { FileName = fileName };

        var sheets = WorkbookReader.ReadAll(fileBytes, fileName);
        if (sheets.Count == 0)
            throw new InvalidOperationException("Workbook has no sheets.");

        // Prefer the sheet whose name contains 'base rate'; else first sheet.
        var sheet = sheets.FirstOrDefault(
                       s => s.Name.Contains("base rate", StringComparison.OrdinalIgnoreCase))
                    ?? sheets[0];
        log.LogInformation("DTC: using sheet '{Name}' ({Rows} rows)", sheet.Name, sheet.Rows.Count);

        // Find the header row — 'Peer Group' in column A, within the first 10 rows.
        int? headerIdx = null;
        for (int i = 0; i < Math.Min(10, sheet.Rows.Count); i++)
        {
            var firstCell = CellCoerce.CleanString(sheet.Rows[i].ElementAtOrDefault(0))?.ToLowerInvariant();
            if (firstCell == "peer group") { headerIdx = i; break; }
        }
        if (!headerIdx.HasValue)
            throw new InvalidOperationException(
                "Could not find header row (expected 'Peer Group' in column A within the first 10 rows).");

        var header = sheet.Rows[headerIdx.Value];
        var meta = new
        {
            PeerGroup = MatchHeader(header, "Peer Group"),
            BaseRateCode = MatchHeader(header, "Base Rate Code"),
            BlendRateCode = MatchHeader(header, "Blend Rate Code"),
            CapitalRateCode = MatchHeader(header, "Capital Rate Code"),
            Region = MatchHeader(header, "Region"),
            CureCode = MatchHeader(header, "Cure Code"),
        };

        if (!meta.PeerGroup.HasValue || !meta.Region.HasValue)
            throw new InvalidOperationException("Required columns 'Peer Group' and/or 'Region' not found.");

        // Identify date columns — every header cell that parses as a date.
        var dateCols = new List<(int colIdx, DateOnly effDate)>();
        for (int i = 0; i < header.Count; i++)
        {
            var d = CellCoerce.ParseHeaderDate(header[i]);
            if (d.HasValue) dateCols.Add((i, d.Value));
        }
        if (dateCols.Count == 0)
            throw new InvalidOperationException("No date columns found in the header row.");
        log.LogInformation("DTC: {Count} date columns ({First} → {Last})",
            dateCols.Count, dateCols[0].effDate, dateCols[^1].effDate);

        // Build the new rows.
        var newRows = new List<ApgBaseRate>();
        var peerGroupsSeen = new HashSet<string>();
        var datesSeen = new HashSet<DateOnly>();

        for (int r = headerIdx.Value + 1; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            if (row.Count == 0) continue;

            var peer = CellCoerce.CleanString(row.ElementAtOrDefault(meta.PeerGroup!.Value));
            if (string.IsNullOrEmpty(peer)) continue;
            // Skip footnote-looking rows
            var lower = peer.ToLowerInvariant();
            if (lower.StartsWith("rate") || lower.StartsWith("note") ||
                lower.StartsWith("*") || lower.StartsWith("source")) continue;

            var region = CellCoerce.CleanString(row.ElementAtOrDefault(meta.Region!.Value));
            if (string.IsNullOrEmpty(region)) continue;

            var baseRc = meta.BaseRateCode.HasValue
                ? CellCoerce.CleanCode(row.ElementAtOrDefault(meta.BaseRateCode.Value)) : null;
            var blendRc = meta.BlendRateCode.HasValue
                ? CellCoerce.CleanCode(row.ElementAtOrDefault(meta.BlendRateCode.Value)) : null;
            var capRc = meta.CapitalRateCode.HasValue
                ? CellCoerce.CleanCode(row.ElementAtOrDefault(meta.CapitalRateCode.Value)) : null;
            var cure = meta.CureCode.HasValue
                ? CellCoerce.CleanCode(row.ElementAtOrDefault(meta.CureCode.Value)) : null;

            peerGroupsSeen.Add(peer);

            foreach (var (colIdx, effDate) in dateCols)
            {
                if (colIdx >= row.Count) continue;
                var rate = CellCoerce.ToDecimal(row[colIdx]);
                if (!rate.HasValue || rate.Value <= 0) continue;
                datesSeen.Add(effDate);
                newRows.Add(new ApgBaseRate
                {
                    Source = "dtc",
                    PeerGroup = peer,
                    CureCode = cure,
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
            throw new InvalidOperationException(
                "Workbook parsed but no DTC base-rate rows extracted. Double-check the sheet layout.");

        // Replace existing source='dtc' rows.
        result.RowsDeleted = await db.ApgBaseRates
            .Where(x => x.Source == "dtc")
            .ExecuteDeleteAsync(ct);

        const int chunkSize = 1_000;
        for (int i = 0; i < newRows.Count; i += chunkSize)
        {
            db.ApgBaseRates.AddRange(newRows.Skip(i).Take(chunkSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        result.RowsInserted = newRows.Count;
        result.DistinctPeerGroups = peerGroupsSeen.Count;
        result.DistinctEffectiveDates = datesSeen.Count;
        result.MostRecentEffectiveDate = datesSeen.Count > 0 ? datesSeen.Max() : null;
        result.Elapsed = stopwatch.Elapsed;

        log.LogInformation(
            "DTC base rates: {Deleted} deleted, {Inserted} inserted ({Pgs} peer groups × {Dates} dates) in {Elapsed:F1}s",
            result.RowsDeleted, result.RowsInserted, result.DistinctPeerGroups,
            result.DistinctEffectiveDates, result.Elapsed.TotalSeconds);

        return result;
    }

    /// <summary>Find the index of a column by header label (case-insensitive,
    /// asterisks ignored).</summary>
    private static int? MatchHeader(IReadOnlyList<object?> header, string wanted)
    {
        var target = wanted.ToLowerInvariant().Replace("*", "").Trim();
        for (int i = 0; i < header.Count; i++)
        {
            var s = CellCoerce.CleanString(header[i]);
            if (s is null) continue;
            var cand = s.ToLowerInvariant().Replace("*", "").Trim();
            if (cand == target) return i;
        }
        return null;
    }
}
