namespace APGAnalyzer.Services.Edi;

/// <summary>
/// Top-level result of parsing one 837 (I or P) file.
/// </summary>
public class Parsed837Result
{
    public string FileType { get; set; } = "837P";   // overridden by GS08 / SV1 vs SV2 detection
    public string InterchangeSender { get; set; } = "";
    public string InterchangeReceiver { get; set; } = "";
    public DateOnly? InterchangeDate { get; set; }
    public string TransactionSetId { get; set; } = "";
    public string ImplementationGuide { get; set; } = "";
    public string SubmitterName { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string BillingProviderName { get; set; } = "";
    public string BillingProviderNpi { get; set; } = "";
    public List<ParsedClaimDto> Claims { get; set; } = new();
}

/// <summary>
/// 837 (Health Care Claim) parser — institutional (837I) and professional
/// (837P). Direct port of backend/parsers/edi_837.py.
///
/// What 837 brings to the table that 835 doesn't:
///   * Diagnosis codes (HI segments) — principal + up to 11 additional
///   * Provider-side info (billing, rendering, pay-to)
///   * No paid/allowed amounts (this is a submission, not a remittance)
///
/// Hierarchy:
///   ISA / GS / ST 837
///   ├── BHT
///   ├── NM1 41 (submitter)
///   ├── NM1 40 (receiver)
///   └── HL* loops:
///       └── HL (billing provider — HL03=20)
///           ├── NM1 85 (billing provider)
///           └── HL (subscriber — HL03=22)
///               ├── NM1 IL (subscriber)
///               ├── NM1 PR (payer)
///               └── (optional) HL (patient — HL03=23)
///                   ├── NM1 QC (patient)
///                   └── CLM loop:
///                       ├── DTP (dates), CL1 (institutional)
///                       ├── HI (diagnosis codes — the key win!)
///                       ├── NM1 82 (rendering provider)
///                       └── LX/SV1 (professional) or LX/SV2 (institutional)
///
/// We flatten the hierarchy: track "current subscriber / current patient
/// / current billing provider" as we walk. Sufficient for claim extraction.
/// </summary>
public sealed class Edi837Parser
{
    private static readonly HashSet<string> ClaimTerminators =
        new(StringComparer.OrdinalIgnoreCase) { "CLM", "HL", "SE", "GE", "IEA" };

    /// <summary>HI segment qualifiers that designate a *principal* diagnosis.</summary>
    private static readonly HashSet<string> PrincipalQualifiers =
        new(StringComparer.OrdinalIgnoreCase) { "ABK", "BK", "ABJ", "BJ", "PR", "APR" };

    private readonly EdiLexer _lex;
    private readonly Parsed837Result _result = new();
    private readonly string? _fileTypeHint;

    public Edi837Parser(string text, string? fileTypeHint = null)
    {
        _lex = new EdiLexer(text);
        _fileTypeHint = fileTypeHint;
        if (!string.IsNullOrEmpty(fileTypeHint)) _result.FileType = fileTypeHint;
    }

    public Parsed837Result Parse()
    {
        var segs = _lex.Segments;

        // Pre-scan: detect file type from GS08 implementation guide, else SV1/SV2 presence
        foreach (var s in segs)
        {
            if (s.Tag == "GS")
            {
                _result.ImplementationGuide = s.Get(8);
                if (_fileTypeHint is null)
                {
                    if (_result.ImplementationGuide.Contains("X222")) _result.FileType = "837P";
                    else if (_result.ImplementationGuide.Contains("X223")) _result.FileType = "837I";
                }
                break;
            }
        }
        if (string.IsNullOrEmpty(_result.ImplementationGuide) && _fileTypeHint is null)
        {
            var hasSv2 = segs.Any(s => s.Tag == "SV2");
            var hasSv1 = segs.Any(s => s.Tag == "SV1");
            if (hasSv2 && !hasSv1) _result.FileType = "837I";
            else if (hasSv1) _result.FileType = "837P";
        }

        int i = 0;
        string currentSubscriberName = "";
        string currentPatientName = "";
        string? currentPatientId = null;

        while (i < segs.Count)
        {
            var s = segs[i];

            switch (s.Tag)
            {
                case "ISA":
                    _result.InterchangeSender   = s.Get(6).Trim();
                    _result.InterchangeReceiver = s.Get(8).Trim();
                    _result.InterchangeDate     = EdiCommon.ParseDate(s.Get(9));
                    break;
                case "ST":
                    _result.TransactionSetId = s.Get(1);
                    break;
                case "NM1":
                    {
                        var entity = s.Get(1);
                        var name = EdiCommon.Nm1Name(s);
                        var ident = EdiCommon.Nm1Id(s);
                        switch (entity)
                        {
                            case "41": _result.SubmitterName = name; break;
                            case "40": _result.ReceiverName = name; break;
                            case "85":
                                _result.BillingProviderName = name;
                                _result.BillingProviderNpi = ident ?? "";
                                break;
                            case "IL": currentSubscriberName = name; break;
                            case "QC":
                                currentPatientName = name;
                                currentPatientId = ident;
                                break;
                        }
                        break;
                    }
                case "CLM":
                    {
                        // Patient defaults to subscriber if no separate patient loop
                        var patientName = !string.IsNullOrEmpty(currentPatientName)
                            ? currentPatientName : currentSubscriberName;
                        i = ParseClm(segs, i, patientName, currentPatientId);
                        continue;
                    }
            }
            i++;
        }
        return _result;
    }

    private int ParseClm(IReadOnlyList<Segment> segs, int start,
                         string patientName, string? patientId)
    {
        var clm = segs[start];
        var claim = new ParsedClaimDto
        {
            FileType = _result.FileType,
            ClaimId = clm.Get(1),
            BilledAmount = EdiCommon.ParseMoney(clm.Get(2)),
            PatientName = string.IsNullOrEmpty(patientName) ? null : patientName,
            PatientId = patientId,
            ProviderName = string.IsNullOrEmpty(_result.BillingProviderName) ? null : _result.BillingProviderName,
            ProviderNpi = string.IsNullOrEmpty(_result.BillingProviderNpi) ? null : _result.BillingProviderNpi,
            // CLM submission has no paid/allowed amounts
            AllowedAmount = 0m,
            PaidAmount = 0m,
            PatientResponsibility = 0m,
        };

        ParsedServiceLineDto? currentLine = null;
        int lineSeq = 0;

        int i = start + 1;
        while (i < segs.Count)
        {
            var s = segs[i];
            if (ClaimTerminators.Contains(s.Tag)) break;

            switch (s.Tag)
            {
                case "HI":
                    foreach (var (qual, code) in ExtractHiDiagnoses(s))
                    {
                        if (PrincipalQualifiers.Contains(qual) &&
                            string.IsNullOrEmpty(claim.PrincipalDiagnosis))
                        {
                            claim.PrincipalDiagnosis = code;
                        }
                        else if (!claim.OtherDiagnoses.Contains(code) && claim.PrincipalDiagnosis != code)
                        {
                            claim.OtherDiagnoses.Add(code);
                        }
                    }
                    break;

                case "NM1":
                    if (s.Get(1) == "82")
                    {
                        // Rendering provider — overrides billing-provider default
                        claim.ProviderName = EdiCommon.Nm1Name(s);
                        claim.ProviderNpi = EdiCommon.Nm1Id(s) ?? "";
                    }
                    break;

                case "DTP":
                    {
                        var qual = s.Get(1);
                        var d = EdiCommon.ParseDtpDate(s);
                        // 472 = service date, 434 = statement from/through
                        if (d.HasValue && (qual == "472" || qual == "434"))
                        {
                            claim.DateOfService ??= d;
                            if (currentLine is not null && currentLine.DateOfService is null)
                                currentLine.DateOfService = d;
                        }
                        break;
                    }

                case "SV1":
                    {
                        // Professional: SV101 composite HCPCS, SV102 charge, SV104 quantity
                        lineSeq++;
                        var (_, procedure, modifiers, _) =
                            EdiCommon.DecodeHcpcsComposite(s, 1);
                        var sl = new ParsedServiceLineDto
                        {
                            LineSeq = lineSeq,
                            ProcedureCode = procedure,
                            Modifiers = modifiers,
                            RevenueCode = null,
                            BilledAmount = EdiCommon.ParseMoney(s.Get(2)),
                            Units = EdiCommon.ParseInt(s.Get(4), 1),
                        };
                        claim.ServiceLines.Add(sl);
                        currentLine = sl;
                        break;
                    }

                case "SV2":
                    {
                        // Institutional: SV201 revenue code, SV202 composite HCPCS,
                        // SV203 charge, SV205 quantity
                        lineSeq++;
                        var revCode = s.Get(1);
                        if (string.IsNullOrEmpty(revCode)) revCode = "";
                        var (_, procedure, modifiers, _) =
                            EdiCommon.DecodeHcpcsComposite(s, 2);
                        var sl = new ParsedServiceLineDto
                        {
                            LineSeq = lineSeq,
                            ProcedureCode = !string.IsNullOrEmpty(procedure) ? procedure : revCode,
                            Modifiers = modifiers,
                            RevenueCode = string.IsNullOrEmpty(revCode) ? null : revCode,
                            BilledAmount = EdiCommon.ParseMoney(s.Get(3)),
                            Units = EdiCommon.ParseInt(s.Get(5), 1),
                        };
                        claim.ServiceLines.Add(sl);
                        currentLine = sl;
                        break;
                    }
            }
            i++;
        }

        _result.Claims.Add(claim);
        return i;
    }

    /// <summary>
    /// Pull diagnosis composites out of a single HI segment. Codes are
    /// canonicalized to uppercase + dot-stripped (matches eMedNY's
    /// crosswalk storage; many submitters include dots they shouldn't).
    /// </summary>
    private static List<(string Qualifier, string Code)> ExtractHiDiagnoses(Segment seg)
    {
        var output = new List<(string, string)>();
        for (int idx = 1; idx <= 12; idx++)
        {
            var raw = seg.Get(idx);
            if (string.IsNullOrEmpty(raw)) continue;
            var parts = raw.Split(':');
            if (parts.Length < 2) continue;
            var qualifier = parts[0].Trim().ToUpperInvariant();
            var code = parts[1].Trim().ToUpperInvariant().Replace(".", "");
            if (!string.IsNullOrEmpty(code))
                output.Add((qualifier, code));
        }
        return output;
    }
}
