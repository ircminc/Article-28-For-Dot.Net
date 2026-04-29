namespace APGAnalyzer.Services.Edi;

/// <summary>
/// 835I parser — Institutional Electronic Remittance Advice.
/// Direct port of backend/parsers/edi_835i.py.
///
/// Hierarchy:
///   ISA / GS / ST 835
///   ├── BPR, TRN, REF, DTM
///   ├── N1 PR (payer), N3, N4, REF, PER
///   ├── N1 PE (payee), N3, N4, REF
///   └── LX header
///       └── CLP (claim payment) — one per claim
///           ├── CAS (claim-level adjustment)
///           ├── NM1 (patient / insured / provider names)
///           ├── REF, DTM (claim-level)
///           └── SVC (service line)
///               ├── DTM (service date)
///               ├── CAS (service-level adjustment)
///               ├── REF (line ref, authorization)
///               └── AMT, LQ, ...
/// </summary>
public sealed class Edi835IParser
{
    /// <summary>Tags that terminate a CLP loop when encountered inside one.</summary>
    private static readonly HashSet<string> ClaimTerminators =
        new(StringComparer.OrdinalIgnoreCase) { "CLP", "LX", "SE", "GE", "IEA" };

    private readonly EdiLexer _lex;
    private readonly Parsed835IResult _result = new();
    private readonly string _fileType;

    public Edi835IParser(string text, string fileType = "835I")
    {
        _lex = new EdiLexer(text);
        _fileType = fileType;
    }

    public Parsed835IResult Parse()
    {
        var segs = _lex.Segments;
        int i = 0;
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
                case "BPR":
                    _result.PaymentMethod = s.Get(4);
                    _result.PaymentAmount = EdiCommon.ParseMoney(s.Get(2));
                    _result.PaymentDate   = EdiCommon.ParseDate(s.Get(16));
                    break;
                case "TRN":
                    _result.CheckEftTrace = s.Get(2);
                    break;
                case "N1":
                    {
                        var entity = s.Get(1);
                        var name = s.Get(2);
                        if (entity == "PR")
                        {
                            _result.PayerName = name;
                            _result.PayerId = s.Get(4);
                        }
                        else if (entity == "PE")
                        {
                            _result.PayeeName = name;
                            _result.PayeeNpi = s.Get(4);
                        }
                        break;
                    }
                case "CLP":
                    i = ParseClp(segs, i);
                    continue;
            }
            i++;
        }
        return _result;
    }

    private int ParseClp(IReadOnlyList<Segment> segs, int start)
    {
        var clp = segs[start];
        var claim = new ParsedClaimDto
        {
            FileType = _fileType,
            ClaimId = clp.Get(1),
            ClaimStatus = clp.Get(2),
            BilledAmount = EdiCommon.ParseMoney(clp.Get(3)),
            PaidAmount = EdiCommon.ParseMoney(clp.Get(4)),
            PatientResponsibility = EdiCommon.ParseMoney(clp.Get(5)),
            ClaimFilingIndicator = string.IsNullOrEmpty(clp.Get(6)) ? null : clp.Get(6),
            PayerName = string.IsNullOrEmpty(_result.PayerName) ? null : _result.PayerName,
            PayerId = string.IsNullOrEmpty(_result.PayerId) ? null : _result.PayerId,
        };

        int i = start + 1;
        ParsedServiceLineDto? currentLine = null;
        int lineSeq = 0;

        while (i < segs.Count)
        {
            var s = segs[i];
            if (ClaimTerminators.Contains(s.Tag)) break;

            switch (s.Tag)
            {
                case "CAS":
                    {
                        var triples = EdiCommon.ExpandCas(s);
                        if (currentLine is null)
                        {
                            // Claim-level adjustment
                            foreach (var (group, reason, amount, qty) in triples)
                                claim.Adjustments.Add(new ParsedAdjustmentDto
                                {
                                    LineSeq = null,
                                    GroupCode = group,
                                    ReasonCode = reason,
                                    Amount = amount,
                                    Quantity = qty,
                                });
                        }
                        else
                        {
                            foreach (var (group, reason, amount, qty) in triples)
                                currentLine.Adjustments.Add(new ParsedAdjustmentDto
                                {
                                    LineSeq = currentLine.LineSeq,
                                    GroupCode = group,
                                    ReasonCode = reason,
                                    Amount = amount,
                                    Quantity = qty,
                                });
                        }
                        break;
                    }

                case "NM1":
                    {
                        var entity = s.Get(1);
                        var fullName = EdiCommon.Nm1Name(s);
                        var nm1Id = EdiCommon.Nm1Id(s);
                        // QC = patient, IL = insured/subscriber, 82 = rendering provider
                        if (entity == "QC")
                        {
                            claim.PatientName = fullName;
                            claim.PatientId = nm1Id;
                        }
                        else if (entity == "82")
                        {
                            claim.ProviderName = fullName;
                            claim.ProviderNpi = nm1Id;
                        }
                        break;
                    }

                case "DTM":
                    {
                        var qual = s.Get(1);
                        var d = EdiCommon.ParseDate(s.Get(2));
                        // 232 = claim statement start, 472 = service date
                        if ((qual == "232" || qual == "472") && d.HasValue)
                        {
                            claim.DateOfService ??= d;
                            if (currentLine is not null && currentLine.DateOfService is null)
                                currentLine.DateOfService = d;
                        }
                        else if (qual == "150" && currentLine is not null)
                        {
                            currentLine.DateOfService = d ?? currentLine.DateOfService;
                        }
                        break;
                    }

                case "AMT":
                    {
                        var qual = s.Get(1);
                        var amt = EdiCommon.ParseMoney(s.Get(2));
                        // AMT*B6 = allowed amount
                        if (qual == "B6")
                        {
                            if (currentLine is not null) currentLine.AllowedAmount = amt;
                            else claim.AllowedAmount = amt;
                        }
                        break;
                    }

                case "SVC":
                    {
                        // SVC01 composite: HC:99213:25 (procedure) or NU:0450 (revenue code)
                        lineSeq++;
                        var (qual, procedure, modifiers, revCode) =
                            EdiCommon.DecodeHcpcsComposite(s, 1);
                        if ((qual == "NU" || qual == "ZZ") && string.IsNullOrEmpty(procedure))
                        {
                            revCode = string.IsNullOrEmpty(s.Composite(1, 2))
                                      ? revCode
                                      : s.Composite(1, 2);
                            procedure = "";
                        }

                        var sl = new ParsedServiceLineDto
                        {
                            LineSeq = lineSeq,
                            ProcedureCode = !string.IsNullOrEmpty(procedure)
                                            ? procedure
                                            : (revCode ?? ""),
                            Modifiers = modifiers,
                            RevenueCode = revCode,
                            BilledAmount = EdiCommon.ParseMoney(s.Get(2)),
                            PaidAmount = EdiCommon.ParseMoney(s.Get(3)),
                            Units = EdiCommon.ParseInt(s.Get(5), 1),
                        };
                        // SVC04 = revenue code (institutional). Senders sometimes
                        // use "0" or "1" as a placeholder on professional claims;
                        // real UB-04 revenue codes are always 3-4 digits.
                        var svc04 = s.Get(4);
                        if (!string.IsNullOrEmpty(svc04) && svc04.Length >= 3)
                            sl.RevenueCode = svc04;

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
}
