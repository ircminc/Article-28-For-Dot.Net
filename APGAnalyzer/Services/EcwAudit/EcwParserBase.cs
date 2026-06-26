using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Base helpers shared by all eCW report parsers.
/// eCW exports prepend 4 spacer columns (Facility / None / None.1 / None.2)
/// before the real data. This base class detects and skips them.
public abstract class EcwParserBase
{
    // eCW adds these as the first 4 columns in most exports
    private static readonly HashSet<string> SpacerNames =
        new(StringComparer.OrdinalIgnoreCase) { "facility", "none", "none.1", "none.2", "" };

    /// Returns the 1-based column index of the first real header cell.
    protected static int FindDataStartCol(IXLRow headerRow)
    {
        foreach (var cell in headerRow.CellsUsed())
        {
            var v = cell.GetString().Trim();
            if (!SpacerNames.Contains(v))
                return cell.Address.ColumnNumber;
        }
        return 1;
    }

    /// Build a dictionary of columnName -> 1-based column index from a header row,
    /// starting at dataStartCol and using case-insensitive keys.
    protected static Dictionary<string, int> BuildColMap(IXLRow headerRow, int dataStartCol)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            if (cell.Address.ColumnNumber < dataStartCol) continue;
            var key = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                map[key] = cell.Address.ColumnNumber;
        }
        return map;
    }

    protected static string Str(IXLRow row, Dictionary<string, int> map, string col)
    {
        if (!map.TryGetValue(col, out var c)) return "";
        return row.Cell(c).GetString().Trim();
    }

    protected static decimal Dec(IXLRow row, Dictionary<string, int> map, string col)
    {
        if (!map.TryGetValue(col, out var c)) return 0m;
        var cell = row.Cell(c);
        if (cell.TryGetValue<decimal>(out var d)) return d;
        if (decimal.TryParse(cell.GetString().Replace(",", ""), out var p)) return p;
        return 0m;
    }

    protected static int Int(IXLRow row, Dictionary<string, int> map, string col)
    {
        if (!map.TryGetValue(col, out var c)) return 0;
        var cell = row.Cell(c);
        if (cell.TryGetValue<int>(out var i)) return i;
        if (int.TryParse(cell.GetString().Trim(), out var p)) return p;
        return 0;
    }

    protected static DateOnly? Date(IXLRow row, Dictionary<string, int> map, string col)
    {
        if (!map.TryGetValue(col, out var c)) return null;
        var cell = row.Cell(c);
        if (cell.TryGetValue<DateTime>(out var dt)) return DateOnly.FromDateTime(dt);
        var s = cell.GetString().Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParse(s, out var parsed)) return DateOnly.FromDateTime(parsed);
        return null;
    }

    protected static bool Bool(IXLRow row, Dictionary<string, int> map, string col)
    {
        var s = Str(row, map, col).ToUpperInvariant();
        return s is "YES" or "TRUE" or "1" or "Y";
    }

    /// Detect whether a row is a summary/grand-total row (last N rows of eCW exports).
    protected static bool IsSummaryRow(IXLRow row, int dataStartCol)
    {
        var first = row.Cell(dataStartCol).GetString().Trim();
        return first.StartsWith("Grand Total", StringComparison.OrdinalIgnoreCase)
            || first.StartsWith("Total", StringComparison.OrdinalIgnoreCase);
    }
}
