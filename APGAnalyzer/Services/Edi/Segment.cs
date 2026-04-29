namespace APGAnalyzer.Services.Edi;

/// <summary>
/// One X12 segment: a tag (e.g. "CLP") and ordered element values.
/// Element accessors are 1-based to match X12 naming (CLP01, CLP02...)
/// so the parser code reads close to the implementation guides.
///
/// Mirrors backend/parsers/_common.py:Segment.
/// </summary>
public sealed class Segment
{
    public string Tag { get; }
    public IReadOnlyList<string> Elements { get; }

    public Segment(string tag, IReadOnlyList<string> elements)
    {
        Tag = tag;
        Elements = elements;
    }

    /// <summary>1-based element accessor matching X12 naming (ISA01, CLP01, ...).</summary>
    public string Get(int idx, string @default = "")
    {
        if (idx <= 0) throw new ArgumentOutOfRangeException(nameof(idx),
            "X12 element positions are 1-based.");
        if (idx - 1 < Elements.Count)
        {
            var v = Elements[idx - 1];
            return string.IsNullOrEmpty(v) ? @default : v;
        }
        return @default;
    }

    /// <summary>
    /// Sub-element accessor for X12 composites: 1-based <paramref name="idx"/>.<paramref name="sub"/>.
    /// X12 composites are ':' separated within a single element.
    /// </summary>
    public string Composite(int idx, int sub, string @default = "")
    {
        var raw = Get(idx, "");
        if (string.IsNullOrEmpty(raw)) return @default;
        var parts = raw.Split(':');
        if (sub - 1 < parts.Length)
        {
            var v = parts[sub - 1];
            return string.IsNullOrEmpty(v) ? @default : v;
        }
        return @default;
    }
}
