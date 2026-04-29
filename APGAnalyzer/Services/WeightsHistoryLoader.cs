using System.Diagnostics;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

public interface IWeightsHistoryLoader
{
    Task<WeightsHistoryLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default);
}

/// <summary>
/// Loads NYS DOH's <c>history_and_fee_schedule.xls</c> (legacy BIFF .xls).
///
/// Three sheets, three target tables:
///   1. "Final APG Based Weights"  → apg_weights        (~21,471 rows)
///   2. "Final Px Based Weights"   → px_based_weights   (~5,278 rows)
///   3. "Fee Schedule"             → fee_schedule       (~2,126 rows)
///
/// Layout for sheet 1 (wide → long):
///   row 0: title
///   row 1: revision note
///   row 2: date headers in cols 2+ (cols 0-1 blank)
///   row 3: "APG" in col 0, "APG Description" in col 1
///   row 4+: data rows  [apg, apg_desc, w_col2, w_col3, ...]
///
/// Layout for sheets 2-3 (paired-column wide → long):
///   row 2: dates in even columns (rows 2, 4, 6...)
///   row 3: labels — col 0 "HCPCS Code", col 1 "HCPCS Description",
///          alternating "Px-Based Weight" / "Units Limit"
///          (or "Reimbursement Amount" / "Max units")
///   row 4+: data, paired columns
///   note: sheet 3 ("Fee Schedule") row 4 is a "(per unit)" subtitle —
///         data starts at row 5.
///
/// Mirrors backend/db/init_weights_history.py exactly.
/// </summary>
public class WeightsHistoryLoader(ApplicationDbContext db, ILogger<WeightsHistoryLoader> log)
    : IWeightsHistoryLoader
{
    private static readonly DateOnly SentinelFinalDate = new(9999, 12, 31);

    public async Task<WeightsHistoryLoadResult> LoadFromBytesAsync(
        byte[] fileBytes, string fileName, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new WeightsHistoryLoadResult { FileName = fileName };

        var sheets = WorkbookReader.ReadAll(fileBytes, fileName);

        var apgSheet = sheets.FirstOrDefault(s => s.Name == "Final APG Based Weights")
            ?? throw new InvalidOperationException("Sheet 'Final APG Based Weights' not found.");
        var pxSheet = sheets.FirstOrDefault(s => s.Name == "Final Px Based Weights")
            ?? throw new InvalidOperationException("Sheet 'Final Px Based Weights' not found.");
        var feeSheet = sheets.FirstOrDefault(s => s.Name == "Fee Schedule")
            ?? throw new InvalidOperationException("Sheet 'Fee Schedule' not found.");

        // Replace-all: wipe each table before inserting fresh rows.
        await db.ApgWeights.ExecuteDeleteAsync(ct);
        await db.PxBasedWeights.ExecuteDeleteAsync(ct);
        await db.FeeSchedule.ExecuteDeleteAsync(ct);

        result.ApgWeightRows = await LoadApgWeights(apgSheet, ct);
        result.PxBasedWeightRows = await LoadPxWeights(pxSheet, ct);
        result.FeeScheduleRows = await LoadFeeSchedule(feeSheet, ct);

        result.Elapsed = stopwatch.Elapsed;
        log.LogInformation(
            "Weights+Fees loaded: {Apg} APG weights, {Px} Px weights, {Fee} fee schedule rows in {Elapsed:F1}s",
            result.ApgWeightRows, result.PxBasedWeightRows, result.FeeScheduleRows,
            result.Elapsed.TotalSeconds);

        return result;
    }

    // -------------------------------------------------------------------
    // Sheet 1: Final APG Based Weights
    // -------------------------------------------------------------------
    private async Task<int> LoadApgWeights(SheetData sheet, CancellationToken ct)
    {
        if (sheet.Rows.Count < 5)
            throw new InvalidOperationException("APG Weights sheet too short.");

        var dateHeaderRow = sheet.Rows[2];
        var fieldHeaderRow = sheet.Rows[3];

        if (CellCoerce.CleanString(fieldHeaderRow.ElementAtOrDefault(0))?.ToUpperInvariant() != "APG")
            throw new InvalidOperationException(
                $"APG Weights sheet: expected 'APG' in row 3 column A; got "
              + $"{CellCoerce.CleanString(fieldHeaderRow.ElementAtOrDefault(0)) ?? "(blank)"}");

        var dateColumns = new List<(int colIdx, DateOnly effDate)>();
        int? finalRateIdx = null;
        int? yearRateIdx = null;

        for (int i = 0; i < dateHeaderRow.Count; i++)
        {
            if (i < 2) continue;
            var raw = CellCoerce.CleanString(dateHeaderRow[i])?.ToLowerInvariant() ?? "";
            if (raw.Contains("final rate")) { finalRateIdx = i; continue; }
            if (raw.Contains("year rate") || raw == "year") { yearRateIdx = i; continue; }
            var d = CellCoerce.ParseHeaderDate(dateHeaderRow[i]);
            if (d.HasValue) dateColumns.Add((i, d.Value));
        }

        log.LogInformation("APG Weights: {Count} date columns; final-rate@{F}, year-rate@{Y}",
            dateColumns.Count, finalRateIdx, yearRateIdx);

        int count = 0;
        var batch = new List<ApgWeight>(2_000);
        for (int r = 4; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            var apg = CellCoerce.ToInt(row.ElementAtOrDefault(0));
            if (!apg.HasValue) continue;
            var desc = CellCoerce.CleanString(row.ElementAtOrDefault(1));

            foreach (var (colIdx, effDate) in dateColumns)
            {
                if (colIdx >= row.Count) continue;
                var w = CellCoerce.ToDecimal(row[colIdx]);
                if (!w.HasValue) continue;
                batch.Add(new ApgWeight
                {
                    Apg = apg.Value,
                    ApgDescription = desc,
                    EffectiveDate = effDate,
                    Weight = w.Value,
                    IsFinalRate = false,
                    YearRate = null,
                });
                count++;
            }

            if (finalRateIdx.HasValue && yearRateIdx.HasValue
                && finalRateIdx.Value < row.Count && yearRateIdx.Value < row.Count)
            {
                var fr = CellCoerce.ToDecimal(row[finalRateIdx.Value]);
                var yr = CellCoerce.ToInt(row[yearRateIdx.Value]);
                if (fr.HasValue && fr.Value > 0 && yr.HasValue)
                {
                    batch.Add(new ApgWeight
                    {
                        Apg = apg.Value,
                        ApgDescription = desc,
                        EffectiveDate = SentinelFinalDate,
                        Weight = fr.Value,
                        IsFinalRate = true,
                        YearRate = yr.Value,
                    });
                    count++;
                }
            }

            if (batch.Count >= 2_000)
            {
                db.ApgWeights.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            db.ApgWeights.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }

    // -------------------------------------------------------------------
    // Paired-column header parser (shared by Px and Fee Schedule sheets)
    // -------------------------------------------------------------------
    private static List<(int valueCol, int secondaryCol, DateOnly effDate)>
        ParsePairedHeader(IReadOnlyList<object?> dateRow, IReadOnlyList<object?> fieldRow)
    {
        var pairs = new List<(int, int, DateOnly)>();
        var max = Math.Max(dateRow.Count, fieldRow.Count);
        for (int i = 2; i < max; i++)
        {
            var d = CellCoerce.ParseHeaderDate(i < dateRow.Count ? dateRow[i] : null);
            if (!d.HasValue) continue;
            pairs.Add((i, i + 1, d.Value));
        }
        return pairs;
    }

    // -------------------------------------------------------------------
    // Sheet 2: Final Px Based Weights
    // -------------------------------------------------------------------
    private async Task<int> LoadPxWeights(SheetData sheet, CancellationToken ct)
    {
        if (sheet.Rows.Count < 5) return 0;
        var dateRow = sheet.Rows[2];
        var fieldRow = sheet.Rows[3];

        var firstField = CellCoerce.CleanString(fieldRow.ElementAtOrDefault(0))?.ToLowerInvariant();
        if (firstField != "hcpcs code")
            throw new InvalidOperationException(
                $"Px Weights sheet: expected 'HCPCS Code' in row 3 column A; got '{firstField}'.");

        var pairs = ParsePairedHeader(dateRow, fieldRow);
        log.LogInformation("Px Weights: {Count} (weight, units) effective-date pairs", pairs.Count);

        int count = 0;
        var batch = new List<PxBasedWeight>(2_000);
        for (int r = 4; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            var hcpcs = CellCoerce.CleanCode(row.ElementAtOrDefault(0))?.ToUpperInvariant();
            if (string.IsNullOrEmpty(hcpcs)) continue;
            var desc = CellCoerce.CleanString(row.ElementAtOrDefault(1));

            foreach (var (weightCol, unitsCol, effDate) in pairs)
            {
                if (weightCol >= row.Count) continue;
                var weight = CellCoerce.ToDecimal(row[weightCol]);
                if (!weight.HasValue || weight.Value <= 0) continue;
                var unitsLimit = unitsCol < row.Count ? CellCoerce.ToDecimal(row[unitsCol]) : null;
                batch.Add(new PxBasedWeight
                {
                    Hcpcs = hcpcs,
                    Description = desc,
                    EffectiveDate = effDate,
                    Weight = weight.Value,
                    UnitsLimit = unitsLimit,
                });
                count++;
            }

            if (batch.Count >= 2_000)
            {
                db.PxBasedWeights.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            db.PxBasedWeights.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }

    // -------------------------------------------------------------------
    // Sheet 3: Fee Schedule
    // -------------------------------------------------------------------
    private async Task<int> LoadFeeSchedule(SheetData sheet, CancellationToken ct)
    {
        if (sheet.Rows.Count < 6) return 0;
        var dateRow = sheet.Rows[2];
        var fieldRow = sheet.Rows[3];

        var firstField = CellCoerce.CleanString(fieldRow.ElementAtOrDefault(0))?.ToLowerInvariant();
        if (firstField != "hcpcs code")
            throw new InvalidOperationException(
                $"Fee Schedule sheet: expected 'HCPCS Code' in row 3 column A; got '{firstField}'.");

        var pairs = ParsePairedHeader(dateRow, fieldRow);
        log.LogInformation("Fee Schedule: {Count} (reimbursement, max_units) effective-date pairs", pairs.Count);

        // Row 4 may be a "(per unit)" subtitle — start at row 5 if so.
        int dataStart = 4;
        if (sheet.Rows.Count > 4 &&
            CellCoerce.CleanString(sheet.Rows[4].ElementAtOrDefault(0)) is null)
        {
            dataStart = 5;
        }

        int count = 0;
        var batch = new List<FeeScheduleItem>(2_000);
        for (int r = dataStart; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            var hcpcs = CellCoerce.CleanCode(row.ElementAtOrDefault(0))?.ToUpperInvariant();
            if (string.IsNullOrEmpty(hcpcs)) continue;
            var desc = CellCoerce.CleanString(row.ElementAtOrDefault(1));

            foreach (var (amtCol, unitsCol, effDate) in pairs)
            {
                if (amtCol >= row.Count) continue;
                var amt = CellCoerce.ToDecimal(row[amtCol]);
                if (!amt.HasValue || amt.Value <= 0) continue;
                var maxUnits = unitsCol < row.Count ? CellCoerce.ToDecimal(row[unitsCol]) : null;
                batch.Add(new FeeScheduleItem
                {
                    Hcpcs = hcpcs,
                    Description = desc,
                    EffectiveDate = effDate,
                    Reimbursement = amt.Value,
                    MaxUnits = maxUnits,
                });
                count++;
            }

            if (batch.Count >= 2_000)
            {
                db.FeeSchedule.AddRange(batch);
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            db.FeeSchedule.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return count;
    }
}
