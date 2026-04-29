using System.Diagnostics;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

/// <summary>
/// Loads the eMedNY APG Crosswalk workbook (APGcrosswalkMMDDYYYY.xlsx).
///
/// Three sheets are read:
///   1. "EAPG Types"          — numeric → string mapping (e.g. "5" → "Incidental")
///   2. "HCPCS to EAPGs"      — ~21,000 rows, populates hcpcs_to_eapg
///   3. "ICD-10 DX to EAPGs"  — ~75,000 rows, populates icd10_to_eapg
///
/// The workbook starts with a few legal-notice rows; the loader scans for
/// the actual header row dynamically (column A == "HCPCS" or "DX"). This
/// makes us resilient to Solventum adding/removing notice rows in future
/// quarterly updates.
///
/// Replace-all semantics: every row from the prior upload is wiped before
/// the new rows go in. Per the user's "Master Reset" requirement, we never
/// want stale crosswalk data sitting alongside fresh data.
///
/// Mirrors backend/db/init_crosswalk.py from the Python service.
/// </summary>
public class CrosswalkLoader(ApplicationDbContext db, ILogger<CrosswalkLoader> log) : ICrosswalkLoader
{
    public async Task<CrosswalkLoadResult> LoadFromBytesAsync(
        byte[] fileBytes,
        string fileName,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new CrosswalkLoadResult { FileName = fileName };

        using var stream = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(stream);

        // ---- 1. Build the EAPG type code → name lookup -------------------
        var typeMap = ReadEapgTypeSheet(workbook);
        result.EapgTypeMappings = typeMap.Count;
        log.LogInformation("Crosswalk: {Count} EAPG type mappings read from sheet (plus fallback)", typeMap.Count);

        // ---- 2. HCPCS → EAPG sheet ---------------------------------------
        var hcpcsRows = ReadHcpcsSheet(workbook, typeMap);
        log.LogInformation("Crosswalk: parsed {Count} HCPCS rows from workbook", hcpcsRows.Count);

        // ---- 3. ICD-10 DX → EAPG sheet -----------------------------------
        var icd10Rows = ReadIcd10Sheet(workbook, typeMap);
        log.LogInformation("Crosswalk: parsed {Count} ICD-10 rows from workbook", icd10Rows.Count);

        // ---- 4. Replace existing rows in a single transaction ------------
        // ExecuteDeleteAsync issues a bulk DELETE without loading entities;
        // for ~95k rows this is the only sensible option.
        result.HcpcsRowsDeleted = await db.HcpcsToEapg.ExecuteDeleteAsync(ct);
        result.Icd10RowsDeleted = await db.Icd10ToEapg.ExecuteDeleteAsync(ct);

        // Bulk insert in chunks. AddRange + SaveChangesAsync is acceptable at
        // 95k rows on LocalDB; for production-scale we can swap in
        // EFCore.BulkExtensions later if profiling shows it's slow.
        const int chunkSize = 2_000;
        for (int i = 0; i < hcpcsRows.Count; i += chunkSize)
        {
            var chunk = hcpcsRows.Skip(i).Take(chunkSize);
            db.HcpcsToEapg.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            // Detach to free memory — otherwise the change tracker holds 95k entities.
            db.ChangeTracker.Clear();
        }
        for (int i = 0; i < icd10Rows.Count; i += chunkSize)
        {
            var chunk = icd10Rows.Skip(i).Take(chunkSize);
            db.Icd10ToEapg.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        result.HcpcsRows = hcpcsRows.Count;
        result.Icd10Rows = icd10Rows.Count;
        result.Elapsed = stopwatch.Elapsed;

        log.LogInformation(
            "Crosswalk loaded: {HcpcsCount} HCPCS rows + {Icd10Count} ICD-10 rows in {Elapsed:F1}s",
            result.HcpcsRows, result.Icd10Rows, result.Elapsed.TotalSeconds);

        return result;
    }

    // -------------------------------------------------------------------
    // Sheet readers
    // -------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> ReadEapgTypeSheet(XLWorkbook wb)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!wb.TryGetWorksheet("EAPG Types", out var sheet)) return map;

        foreach (var row in sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>())
        {
            var codeCell = row.Cell(1).GetString().Trim();
            var nameCell = row.Cell(2).GetString().Trim();
            // Skip the header ("EAPG Type" / "Description") and any non-data rows
            if (string.IsNullOrEmpty(codeCell)) continue;
            if (!int.TryParse(codeCell, out _)) continue;
            if (string.IsNullOrEmpty(nameCell)) continue;
            map[codeCell] = nameCell;
        }
        return map;
    }

    private static List<HcpcsToEapg> ReadHcpcsSheet(XLWorkbook wb, IReadOnlyDictionary<string, string> typeMap)
    {
        var sheet = wb.Worksheet("HCPCS to EAPGs");
        var headerIdx = FindHeaderRow(sheet, "HCPCS");
        var cols = MapColumns(sheet, headerIdx, new[]
        {
            "HCPCS", "Description", "EAPG", "EAPG Type", "EAPG Category",
            "Eapg Service Line", "Quarter Effective Date", "Quarter End Date",
            "Mid-Quarter Effective Date", "Mid-Quarter End Date",
        });

        var rows = new List<HcpcsToEapg>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerIdx;
        for (int r = headerIdx + 1; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var hcpcs = row.Cell(cols["HCPCS"]).GetString().Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(hcpcs)) continue;
            if (!int.TryParse(row.Cell(cols["EAPG"]).GetString().Trim(), out var eapg)) continue;

            rows.Add(new HcpcsToEapg
            {
                Hcpcs = hcpcs,
                Description = NullIfBlank(row.Cell(cols["Description"]).GetString()),
                Eapg = eapg,
                EapgType = EapgTypeMap.Resolve(row.Cell(cols["EAPG Type"]).GetString(), typeMap),
                EapgCategory = NullIfBlank(row.Cell(cols["EAPG Category"]).GetString()),
                EapgServiceLine = NullIfBlank(row.Cell(cols["Eapg Service Line"]).GetString()),
                QuarterEffectiveDate = ParseDate(row.Cell(cols["Quarter Effective Date"])),
                QuarterEndDate = ParseDate(row.Cell(cols["Quarter End Date"])),
                MidQuarterEffectiveDate = ParseDate(row.Cell(cols["Mid-Quarter Effective Date"])),
                MidQuarterEndDate = ParseDate(row.Cell(cols["Mid-Quarter End Date"])),
            });
        }
        return rows;
    }

    private static List<Icd10ToEapg> ReadIcd10Sheet(XLWorkbook wb, IReadOnlyDictionary<string, string> typeMap)
    {
        var sheet = wb.Worksheet("ICD-10 DX to EAPGs");
        var headerIdx = FindHeaderRow(sheet, "DX");
        var cols = MapColumns(sheet, headerIdx, new[]
        {
            "DX", "Description", "Gender", "EAPG", "EAPG Type", "EAPG Category",
            "EAPG Service Line", "Effective Date", "End Date",
        });

        var rows = new List<Icd10ToEapg>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerIdx;
        for (int r = headerIdx + 1; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var rawDx = row.Cell(cols["DX"]).GetString();
            var dx = DxCodeNormalizer.Normalize(rawDx);
            if (string.IsNullOrEmpty(dx)) continue;
            if (!int.TryParse(row.Cell(cols["EAPG"]).GetString().Trim(), out var eapg)) continue;

            rows.Add(new Icd10ToEapg
            {
                DxCode = dx,
                Description = NullIfBlank(row.Cell(cols["Description"]).GetString()),
                Gender = NullIfBlank(row.Cell(cols["Gender"]).GetString()),
                Eapg = eapg,
                EapgType = EapgTypeMap.Resolve(row.Cell(cols["EAPG Type"]).GetString(), typeMap),
                EapgCategory = NullIfBlank(row.Cell(cols["EAPG Category"]).GetString()),
                EapgServiceLine = NullIfBlank(row.Cell(cols["EAPG Service Line"]).GetString()),
                EffectiveDate = ParseDate(row.Cell(cols["Effective Date"])),
                EndDate = ParseDate(row.Cell(cols["End Date"])),
            });
        }
        return rows;
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Scans the first 30 rows for one whose column A matches the given
    /// header-token (case-insensitive). The Solventum workbook prepends a
    /// few legal-notice rows; this lets us tolerate that without hardcoding
    /// row numbers.
    /// </summary>
    private static int FindHeaderRow(IXLWorksheet sheet, string headerToken)
    {
        for (int r = 1; r <= 30; r++)
        {
            var v = sheet.Cell(r, 1).GetString().Trim();
            if (string.Equals(v, headerToken, StringComparison.OrdinalIgnoreCase)) return r;
        }
        throw new InvalidOperationException(
            $"Could not find header row containing '{headerToken}' in column A of sheet '{sheet.Name}'. " +
            $"The workbook layout may have changed.");
    }

    private static IReadOnlyDictionary<string, int> MapColumns(IXLWorksheet sheet, int headerRow, string[] expected)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Walk every cell on the header row up to its last used column.
        var lastCol = sheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (int c = 1; c <= lastCol; c++)
        {
            var label = sheet.Cell(headerRow, c).GetString().Trim();
            if (!string.IsNullOrEmpty(label) && !map.ContainsKey(label)) map[label] = c;
        }
        // Sanity-check: every required column must be present.
        var missing = expected.Where(e => !map.ContainsKey(e)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Sheet '{sheet.Name}' is missing required column(s): {string.Join(", ", missing)}. " +
                $"Available columns: {string.Join(", ", map.Keys)}");
        }
        return map;
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateOnly? ParseDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        // Excel dates may be stored as DateTime, as a serial number, or as a string.
        try
        {
            if (cell.DataType == XLDataType.DateTime)
                return DateOnly.FromDateTime(cell.GetDateTime());
            if (cell.DataType == XLDataType.Number && cell.GetDouble() > 0)
                return DateOnly.FromDateTime(DateTime.FromOADate(cell.GetDouble()));
            var s = cell.GetString().Trim();
            if (string.IsNullOrEmpty(s) || s == " ") return null;
            // Solventum's source uses "Jul 1, 2020"-style strings on some rows.
            if (DateTime.TryParse(s, out var dt)) return DateOnly.FromDateTime(dt);
        }
        catch { /* fall through to null */ }
        return null;
    }
}
