using System.Globalization;

namespace APGAnalyzer.Services.Edi;

/// <summary>
/// Shared element decoders + segment helpers used by 835I/835P/837 parsers.
/// Mirrors backend/parsers/_common.py — minus the lexer + Segment which
/// already live in their own files.
/// </summary>
public static class EdiCommon
{
    /// <summary>Parse CCYYMMDD or YYMMDD; range form keeps the start date.</summary>
    public static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Contains('-')) s = s.Split('-', 2)[0].Trim();
        foreach (var fmt in new[] { "yyyyMMdd", "yyMMdd" })
        {
            if (DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                return DateOnly.FromDateTime(dt);
        }
        return null;
    }

    /// <summary>
    /// Decode a DTP segment (837 dates). Format qualifier in DTP02:
    ///   D8  = single date CCYYMMDD
    ///   RD8 = range CCYYMMDD-CCYYMMDD (returns start)
    ///   DT  = datetime CCYYMMDDHHMM (returns date)
    /// </summary>
    public static DateOnly? ParseDtpDate(Segment seg)
    {
        var fmt = seg.Get(2).Trim().ToUpperInvariant();
        var raw = seg.Get(3);
        if (string.IsNullOrEmpty(raw)) return null;
        if (fmt == "D8" || fmt == "") return ParseDate(raw);
        if (fmt == "RD8")
        {
            var idx = raw.IndexOf('-');
            return ParseDate(idx >= 0 ? raw[..idx] : raw);
        }
        if (fmt == "DT") return ParseDate(raw.Length >= 8 ? raw[..8] : raw);
        return ParseDate(raw);
    }

    /// <summary>Money decoder: empty / unparseable → 0m.</summary>
    public static decimal ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        return decimal.TryParse(raw.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    /// <summary>Int decoder; default for empty / unparseable.</summary>
    public static int ParseInt(string? raw, int @default = 0)
    {
        if (string.IsNullOrWhiteSpace(raw)) return @default;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        // Some senders write "1.0" — tolerate decimals too
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (int)d;
        return @default;
    }

    /// <summary>
    /// CAS expansion: a CAS segment can carry up to 6 (reason, amount, qty)
    /// triplets sharing CAS01's group code. Returns one tuple per triplet.
    /// </summary>
    public static List<(string Group, string Reason, decimal Amount, int? Quantity)>
        ExpandCas(Segment seg)
    {
        var group = seg.Get(1);
        var triples = new List<(string, string, decimal, int?)>();
        for (int start = 2; start < 20; start += 3)
        {
            var reason = seg.Get(start);
            if (string.IsNullOrEmpty(reason)) continue;
            var amt = ParseMoney(seg.Get(start + 1));
            var qtyRaw = seg.Get(start + 2);
            int? qty = string.IsNullOrEmpty(qtyRaw) ? null : ParseInt(qtyRaw);
            triples.Add((group, reason, amt, qty));
        }
        return triples;
    }

    /// <summary>
    /// Assemble NM1 display name. NM1*02='1' = person → "First Middle Last";
    /// otherwise organization → NM1*03 only.
    /// </summary>
    public static string Nm1Name(Segment seg)
    {
        var lastOrOrg = seg.Get(3);
        var first = seg.Get(4);
        var middle = seg.Get(5);
        var entityType = seg.Get(2);
        if (entityType == "1")
        {
            var parts = new[] { first, middle, lastOrOrg }
                .Where(p => !string.IsNullOrEmpty(p));
            return string.Join(" ", parts);
        }
        return lastOrOrg;
    }

    /// <summary>NM1 identification code (NM109 — NPI, MI, etc.).</summary>
    public static string? Nm1Id(Segment seg)
    {
        var v = seg.Get(9);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// Decode a 'HC:99213:25:59'-style composite into
    /// (qualifier, procedure, modifiers, revenueCodeOrNull).
    /// Qualifiers: HC=HCPCS/CPT, NU=UB-04 revenue code, ZZ=mutually defined,
    /// ER=emergency revenue, N4=National Drug Code.
    /// </summary>
    public static (string Qualifier, string Procedure, List<string> Modifiers, string? RevenueCode)
        DecodeHcpcsComposite(Segment seg, int elementIdx)
    {
        var qual = seg.Composite(elementIdx, 1);
        var primary = seg.Composite(elementIdx, 2);
        var mods = new[]
        {
            seg.Composite(elementIdx, 3),
            seg.Composite(elementIdx, 4),
            seg.Composite(elementIdx, 5),
            seg.Composite(elementIdx, 6),
        }.Where(m => !string.IsNullOrEmpty(m)).ToList();
        string? revCode = null;
        if ((qual == "NU" || qual == "ZZ") && string.IsNullOrEmpty(primary))
            revCode = seg.Composite(elementIdx, 2);
        return (qual, primary, mods, revCode);
    }
}
