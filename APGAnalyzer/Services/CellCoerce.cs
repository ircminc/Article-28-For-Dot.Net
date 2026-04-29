using System.Globalization;
using System.Text.RegularExpressions;

namespace APGAnalyzer.Services;

/// <summary>
/// Cell-value coercion helpers shared by every reference-data loader.
/// Centralizing these keeps "what counts as a number / date / blank"
/// consistent across the Crosswalk, Weights+Fees, DTC, and PMTAC loaders
/// — and makes parity testing against the Python service tractable.
/// </summary>
public static class CellCoerce
{
    /// <summary>Trim, treat empty / "-" / whitespace as null, otherwise return the trimmed string.</summary>
    public static string? CleanString(object? value)
    {
        if (value is null) return null;
        var s = value switch
        {
            string str => str,
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTime dt => dt.ToString("yyyy-MM-dd"),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        s = s.Trim();
        if (s == "" || s == "-") return null;
        return s;
    }

    /// <summary>Like CleanString but also strips a trailing ".0" so Excel-stored
    /// integers ('1428.0' from a numeric cell) come back as "1428".</summary>
    public static string? CleanCode(object? value)
    {
        if (value is null) return null;
        if (value is double dbl && dbl == Math.Floor(dbl))
            return ((long)dbl).ToString(CultureInfo.InvariantCulture);
        return CleanString(value);
    }

    /// <summary>Try to read a cell as int. Empty / unparseable → null.</summary>
    public static int? ToInt(object? value)
    {
        if (value is null) return null;
        if (value is double d) return (int)d;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        var s = CleanString(value);
        if (s is null) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        // Sometimes encoded as a float string ("491.0")
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return (int)f;
        return null;
    }

    /// <summary>Try to read a cell as decimal. Used for weights, rates, money.</summary>
    public static decimal? ToDecimal(object? value)
    {
        if (value is null) return null;
        if (value is decimal dec) return dec;
        if (value is double dbl) return (decimal)dbl;
        if (value is int i) return i;
        if (value is long l) return l;
        var s = CleanString(value);
        if (s is null) return null;
        if (decimal.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    private static readonly Regex DateRegex = new(
        @"(?<m>\d{1,2})[/-](?<d>\d{1,2})[/-](?<y>\d{4})|" +
        @"(?<y2>\d{4})[/-](?<m2>\d{1,2})[/-](?<d2>\d{1,2})",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, int> MonthAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"]=1,["january"]=1,["feb"]=2,["february"]=2,["mar"]=3,["march"]=3,
        ["apr"]=4,["april"]=4,["may"]=5,["jun"]=6,["june"]=6,["jul"]=7,["july"]=7,
        ["aug"]=8,["august"]=8,["sep"]=9,["sept"]=9,["september"]=9,
        ["oct"]=10,["october"]=10,["nov"]=11,["november"]=11,["dec"]=12,["december"]=12,
    };

    /// <summary>
    /// Parse a "header-row" cell that's expected to carry an effective
    /// date. Tolerates:
    ///   * Native DateOnly / DateTime cells
    ///   * Numeric/string slash dates ("4/1/2022", "2022-04-01")
    ///   * Footnote markers ("4/1/2022****")
    ///   * Multi-line strings ("Dec 1\n2008", "July 1 \n2011")
    ///   * "Final Rate" / "Year Rate" header cells (returns null)
    ///
    /// Mirrors _header_to_date / _header_date in the Python service.
    /// </summary>
    public static DateOnly? ParseHeaderDate(object? value)
    {
        if (value is null) return null;
        if (value is DateOnly d) return d;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);

        var s = CleanString(value);
        if (s is null) return null;
        s = s.Replace("\n", " ").Trim().TrimEnd('*').Trim();
        if (string.IsNullOrEmpty(s)) return null;

        var lower = s.ToLowerInvariant();
        if (lower.Contains("final") || lower.Contains("year") || lower.Contains("effective"))
            return null;

        // Slash/hyphen date like "4/1/2022"
        var m = DateRegex.Match(s);
        if (m.Success)
        {
            try
            {
                if (m.Groups["y"].Success)
                    return new DateOnly(int.Parse(m.Groups["y"].Value), int.Parse(m.Groups["m"].Value), int.Parse(m.Groups["d"].Value));
                return new DateOnly(int.Parse(m.Groups["y2"].Value), int.Parse(m.Groups["m2"].Value), int.Parse(m.Groups["d2"].Value));
            }
            catch { /* fall through */ }
        }

        // Word-month form like "Dec 1 2008", "July 1 2011", "Jan 1, 2026"
        var wm = Regex.Match(s, @"([A-Za-z]+)\s*(\d*)\s*,?\s*(\d{4})");
        if (wm.Success && MonthAliases.TryGetValue(wm.Groups[1].Value, out var month))
        {
            var day = wm.Groups[2].Value;
            if (string.IsNullOrEmpty(day)) day = "1";
            try { return new DateOnly(int.Parse(wm.Groups[3].Value), month, int.Parse(day)); }
            catch { /* fall through */ }
        }
        return null;
    }
}
