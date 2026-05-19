using System.Text;

namespace APGAnalyzer.Services.Edi;

/// <summary>
/// Inspects an EDI X12 file's header segments to decide which specific
/// 837 / 835 subtype it represents, given the user's high-level family
/// hint ("835" or "837").
///
/// 837 (claims) carries the implementation-guide identifier in the GS08
/// segment, which is unambiguous:
///   005010X222A1 → 837P (professional)
///   005010X223A2 → 837I (institutional)
/// We fall back to SV1 vs SV2 service-line segments if GS08 is missing
/// or non-standard.
///
/// 835 (remits) uses a single implementation guide (005010X221A1) for
/// both institutional and professional remits — the format itself does
/// NOT distinguish them. We heuristic-detect from the SVC procedure-code
/// qualifier:
///   any SVC*NU*…  → 835I (revenue codes — UB-04 / institutional)
///   only SVC*HC*… → 835P (HCPCS — typically professional, but also valid for I)
/// On ambiguous files we default to 835I, which matches the Article 28
/// institutional context this app is built for.
/// </summary>
public static class EdiFileTypeDetector
{
    public enum DetectionConfidence { High, Medium, Low }

    public sealed class DetectionResult
    {
        public string FileType { get; init; } = "";        // 835I | 835P | 837I | 837P
        public DetectionConfidence Confidence { get; init; }
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// Detect the specific subtype. <paramref name="family"/> is the user
    /// dropdown choice ("835" or "837"); we narrow it to I or P.
    /// </summary>
    public static DetectionResult Detect(byte[] fileBytes, string family)
    {
        var text = Encoding.UTF8.GetString(fileBytes);
        family = (family ?? "").Trim().ToUpperInvariant();

        return family switch
        {
            "837" => Detect837(text),
            "835" => Detect835(text),
            _     => throw new ArgumentException(
                $"Unknown family '{family}'. Use '835' or '837'.", nameof(family)),
        };
    }

    // ---------------------------------------------------------------
    // 837 detection — GS08 implementation guide is authoritative
    // ---------------------------------------------------------------
    private static DetectionResult Detect837(string text)
    {
        // GS08 contains something like '005010X222A1' (P) or '005010X223A2' (I)
        var (segs, _) = SplitSegments(text);
        foreach (var seg in segs)
        {
            if (seg.StartsWith("GS", StringComparison.Ordinal))
            {
                var parts = seg.Split('*');
                if (parts.Length >= 9)
                {
                    var ig = parts[8];
                    if (ig.Contains("X222"))
                        return new DetectionResult
                        {
                            FileType = "837P",
                            Confidence = DetectionConfidence.High,
                            Reason = $"GS08 implementation guide '{ig}' = professional",
                        };
                    if (ig.Contains("X223"))
                        return new DetectionResult
                        {
                            FileType = "837I",
                            Confidence = DetectionConfidence.High,
                            Reason = $"GS08 implementation guide '{ig}' = institutional",
                        };
                }
                break;   // only one GS at the envelope level
            }
        }

        // Fallback: SV2 → institutional, SV1 → professional
        var hasSv2 = segs.Any(s => s.StartsWith("SV2", StringComparison.Ordinal));
        var hasSv1 = segs.Any(s => s.StartsWith("SV1", StringComparison.Ordinal));
        if (hasSv2)
            return new DetectionResult
            {
                FileType = "837I",
                Confidence = DetectionConfidence.Medium,
                Reason = "Found SV2 service-line segments (institutional)",
            };
        if (hasSv1)
            return new DetectionResult
            {
                FileType = "837P",
                Confidence = DetectionConfidence.Medium,
                Reason = "Found SV1 service-line segments (professional)",
            };

        // Last resort
        return new DetectionResult
        {
            FileType = "837P",
            Confidence = DetectionConfidence.Low,
            Reason = "GS08 missing and no SV1/SV2 segments — defaulted to 837P",
        };
    }

    // ---------------------------------------------------------------
    // 835 detection — heuristic from SVC procedure-code qualifier
    // ---------------------------------------------------------------
    private static DetectionResult Detect835(string text)
    {
        var (segs, _) = SplitSegments(text);

        // SVC*<composite>*…  where composite is "HC:90837" or "NU:0450" etc.
        // Composite separator is the ':' inside the first sub-element.
        int nuCount = 0, hcCount = 0;
        foreach (var seg in segs)
        {
            if (!seg.StartsWith("SVC", StringComparison.Ordinal)) continue;
            var parts = seg.Split('*');
            if (parts.Length < 2) continue;
            var composite = parts[1];
            var qualifier = composite.Split(':', '^', '>')[0].ToUpperInvariant();
            if (qualifier == "NU") nuCount++;
            else if (qualifier == "HC") hcCount++;
        }

        if (nuCount > 0)
            return new DetectionResult
            {
                FileType = "835I",
                Confidence = DetectionConfidence.High,
                Reason = $"Found {nuCount} SVC*NU (revenue-code) line(s) — institutional",
            };

        if (hcCount > 0)
            return new DetectionResult
            {
                FileType = "835P",
                Confidence = DetectionConfidence.Medium,
                Reason = $"All {hcCount} SVC line(s) use HCPCS qualifier — likely professional "
                       + "(institutional 835s with HCPCS-only services are also valid; "
                       + "edit if needed)",
            };

        return new DetectionResult
        {
            FileType = "835I",
            Confidence = DetectionConfidence.Low,
            Reason = "No SVC lines found to inspect — defaulted to 835I",
        };
    }

    // ---------------------------------------------------------------
    // Segment splitter (handles ~ \n \r\n with optional trailing whitespace)
    // ---------------------------------------------------------------
    private static (string[] Segments, char Separator) SplitSegments(string text)
    {
        // Standard X12 segment terminator is '~', but production files often
        // use newlines too. Either works for our header peek.
        char sep = '~';
        if (!text.Contains('~') && text.Contains('\n')) sep = '\n';

        return (
            text.Split(sep, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim('\r', '\n', ' ', '\t'))
                .Where(s => s.Length >= 2)
                .ToArray(),
            sep);
    }
}
