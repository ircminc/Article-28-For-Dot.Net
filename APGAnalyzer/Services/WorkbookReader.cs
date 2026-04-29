using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using DateOnly = System.DateOnly;

namespace APGAnalyzer.Services;

/// <summary>
/// One sheet, parsed into a 2-D array of cell values. Cell values are
/// boxed objects so the loaders can downcast to the type they expect:
///     - DateOnly  for date-shaped cells
///     - double    for numeric cells
///     - string    for text
///     - null      for empty cells
///
/// Row 0 is the first row of the sheet (matches Python's 0-indexed view).
/// </summary>
public class SheetData
{
    public required string Name { get; init; }
    public required List<List<object?>> Rows { get; init; }

    /// <summary>
    /// Safe accessor — returns null for out-of-range indices so loaders
    /// don't have to bounds-check every cell themselves.
    /// </summary>
    public object? Cell(int rowIdx, int colIdx)
    {
        if (rowIdx < 0 || rowIdx >= Rows.Count) return null;
        var row = Rows[rowIdx];
        if (colIdx < 0 || colIdx >= row.Count) return null;
        return row[colIdx];
    }
}

/// <summary>
/// Reads either an .xlsx (via ClosedXML) or a legacy .xls (via NPOI) into
/// a uniform list of SheetData. Mirrors the helper Python uses when it
/// dispatches to openpyxl vs xlrd based on filename.
/// </summary>
public static class WorkbookReader
{
    public static IReadOnlyList<SheetData> ReadAll(byte[] fileBytes, string fileName)
    {
        if (fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            return ReadXls(fileBytes);
        // Default to .xlsx for .xlsx, .xlsm, and unknown extensions
        return ReadXlsx(fileBytes);
    }

    // -----------------------------------------------------------------
    // .xlsx via ClosedXML
    // -----------------------------------------------------------------
    private static IReadOnlyList<SheetData> ReadXlsx(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes);
        using var wb = new XLWorkbook(ms);
        var result = new List<SheetData>();
        foreach (var sheet in wb.Worksheets)
        {
            var rows = new List<List<object?>>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int r = 1; r <= lastRow; r++)
            {
                var row = new List<object?>(lastCol);
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = sheet.Cell(r, c);
                    row.Add(GetClosedXmlValue(cell));
                }
                rows.Add(row);
            }
            result.Add(new SheetData { Name = sheet.Name, Rows = rows });
        }
        return result;
    }

    private static object? GetClosedXmlValue(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        return cell.DataType switch
        {
            XLDataType.DateTime => DateOnly.FromDateTime(cell.GetDateTime()),
            XLDataType.Number   => cell.GetDouble(),
            XLDataType.Boolean  => cell.GetBoolean(),
            _                    => cell.GetString(),
        };
    }

    // -----------------------------------------------------------------
    // .xls (legacy BIFF) via NPOI
    // -----------------------------------------------------------------
    private static IReadOnlyList<SheetData> ReadXls(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes);
        // HSSFWorkbook = .xls (BIFF). XSSFWorkbook would be .xlsx via NPOI.
        IWorkbook wb = new HSSFWorkbook(ms);
        var result = new List<SheetData>();
        for (int s = 0; s < wb.NumberOfSheets; s++)
        {
            var sheet = wb.GetSheetAt(s);
            var rows = new List<List<object?>>();
            var lastRow = sheet.LastRowNum;       // 0-indexed in NPOI
            for (int r = 0; r <= lastRow; r++)
            {
                var sheetRow = sheet.GetRow(r);
                if (sheetRow is null)
                {
                    rows.Add(new List<object?>());
                    continue;
                }
                var lastCellNum = sheetRow.LastCellNum; // exclusive upper bound
                var row = new List<object?>(lastCellNum);
                for (int c = 0; c < lastCellNum; c++)
                {
                    var cell = sheetRow.GetCell(c);
                    row.Add(GetNpoiValue(cell));
                }
                rows.Add(row);
            }
            result.Add(new SheetData { Name = sheet.SheetName, Rows = rows });
        }
        return result;
    }

    private static object? GetNpoiValue(ICell? cell)
    {
        if (cell is null) return null;
        return cell.CellType switch
        {
            CellType.Blank   => null,
            CellType.String  => cell.StringCellValue,
            CellType.Boolean => cell.BooleanCellValue,
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                 ? (object)DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.MinValue)
                                 : cell.NumericCellValue,
            CellType.Formula => GetFormulaValue(cell),
            _                 => null,
        };
    }

    private static object? GetFormulaValue(ICell cell)
    {
        // Use the CACHED formula result if Excel saved one, which it almost
        // always does for files like history_and_fee_schedule.xls.
        return cell.CachedFormulaResultType switch
        {
            CellType.String  => cell.StringCellValue,
            CellType.Boolean => cell.BooleanCellValue,
            CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                                 ? (object)DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.MinValue)
                                 : cell.NumericCellValue,
            _                 => null,
        };
    }
}
