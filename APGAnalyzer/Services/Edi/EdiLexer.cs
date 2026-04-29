namespace APGAnalyzer.Services.Edi;

/// <summary>
/// Tokenize a raw X12 document into <see cref="Segment"/> objects.
///
/// Auto-detects delimiters from the ISA envelope:
///   - ISA[3]   = element separator (col 4)
///   - ISA[104] = sub-element separator (col 105)
///   - ISA[105] = segment terminator (col 106)
///
/// Tolerates a leading BOM / whitespace before ISA, and intra-segment
/// newlines (some senders prettify with line breaks).
///
/// Mirrors EDILexer in backend/parsers/_common.py.
/// </summary>
public sealed class EdiLexer
{
    public char ElementSep { get; }
    public char SubelementSep { get; }
    public char SegmentSep { get; }
    public IReadOnlyList<Segment> Segments { get; }

    public EdiLexer(string text)
    {
        var stripped = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (!stripped.StartsWith("ISA"))
            throw new ArgumentException(
                "Input does not start with an ISA segment (not a valid X12 interchange).");
        if (stripped.Length < 106)
            throw new ArgumentException("Input too short to contain an ISA header.");

        ElementSep    = stripped[3];
        SubelementSep = stripped[104];
        SegmentSep    = stripped[105];

        var normalized = stripped.Replace("\r", "").Replace("\n", "");
        var raw = normalized.Split(SegmentSep);
        var list = new List<Segment>(raw.Length);
        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            var parts = r.Split(ElementSep);
            var tag = parts[0].Trim();
            if (string.IsNullOrEmpty(tag)) continue;
            var elements = parts.Length > 1
                ? parts.Skip(1).ToArray()
                : Array.Empty<string>();
            list.Add(new Segment(tag, elements));
        }
        Segments = list;
    }
}
