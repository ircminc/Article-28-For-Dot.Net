using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

/// <summary>
/// The APG calculation engine — direct port of backend/engines/apg_engine.py.
///
/// Implements the NYS DOH Article 28 / APG methodology with:
///   * Date-scoped HCPCS / ICD-10 / weight / base-rate lookups
///   * Pricing priority ladder:
///       1. Fee Schedule  (flat reimbursement × units, bypasses APG)
///       2. Px-Based Weight (overrides APG weight)
///       3. APG Weight    (classic base_rate × weight)
///   * Visit-purpose ICD override for Incidental E/M placeholders (the
///     99213+E11.9 → $132.09 fix)
///   * Packaging — Incidental EAPGs and zero-weight codes are not paid
///   * Multi-procedure discounting — secondaries pay at 50%
///   * Medical-Visit packages into Significant-Procedure on same claim
///   * Modifier U6 — adjustment factor
///   * Capital add-on — flat per-claim amount
///
/// All money math uses decimal with HALF_UP rounding at 2dp.
/// </summary>
public class ApgEngine(ApplicationDbContext db, ILogger<ApgEngine> log) : IApgEngine
{
    // Per NYS DOH U6 Modifier Policy. 0.75 is a placeholder; refine when
    // the policy rate is officially confirmed.
    private static readonly decimal U6AdjustmentFactor = 0.75m;
    private static readonly decimal MultiProcedureDiscount = 0.50m;
    private static readonly DateOnly SentinelFinalDate = new(9999, 12, 31);

    // -----------------------------------------------------------------
    // Date-scoped lookups
    // -----------------------------------------------------------------

    public async Task<HcpcsToEapg?> LookupHcpcsAsync(string hcpcs, DateOnly dos, CancellationToken ct = default)
    {
        return await db.HcpcsToEapg
            .Where(x => x.Hcpcs == hcpcs)
            .Where(x => x.QuarterEffectiveDate == null || x.QuarterEffectiveDate <= dos)
            .Where(x => x.QuarterEndDate       == null || x.QuarterEndDate       >= dos)
            .OrderByDescending(x => x.QuarterEffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Icd10ToEapg?> LookupIcd10Async(string? rawDx, DateOnly dos, CancellationToken ct = default)
    {
        var key = DxCodeNormalizer.Normalize(rawDx);
        if (string.IsNullOrEmpty(key)) return null;

        return await db.Icd10ToEapg
            .Where(x => x.DxCode == key)
            .Where(x => x.EffectiveDate == null || x.EffectiveDate <= dos)
            .Where(x => x.EndDate       == null || x.EndDate       >= dos)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ApgWeight?> LookupApgWeightAsync(int apg, DateOnly dos, CancellationToken ct = default)
    {
        // 1. Final-rate row wins when its year_rate >= year(DOS).
        var final = await db.ApgWeights
            .Where(x => x.Apg == apg && x.IsFinalRate && x.YearRate != null && x.YearRate >= dos.Year)
            .FirstOrDefaultAsync(ct);
        if (final is not null) return final;

        // 2. Otherwise, the most-recent dated row <= DOS.
        return await db.ApgWeights
            .Where(x => x.Apg == apg && !x.IsFinalRate && x.EffectiveDate <= dos)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ApgBaseRate?> LookupBaseRateAsync(
        string source, string peerGroup, string region, DateOnly dos, CancellationToken ct = default)
    {
        return await db.ApgBaseRates
            .Where(x => x.Source     == source)
            .Where(x => x.PeerGroup  == peerGroup)    // exact match — never fuzz
            .Where(x => x.Region     == region)
            .Where(x => x.EffectiveDate <= dos)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<FeeScheduleItem?> LookupFeeScheduleAsync(string hcpcs, DateOnly dos, CancellationToken ct = default)
    {
        return await db.FeeSchedule
            .Where(x => x.Hcpcs == hcpcs && x.EffectiveDate <= dos)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PxBasedWeight?> LookupPxWeightAsync(string hcpcs, DateOnly dos, CancellationToken ct = default)
    {
        return await db.PxBasedWeights
            .Where(x => x.Hcpcs == hcpcs && x.EffectiveDate <= dos)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> ResolveRegionAsync(ProviderConfig provider, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(provider.Region)) return provider.Region;
        if (provider.CountyCode.HasValue)
        {
            var row = await db.ProviderCounties
                .FirstOrDefaultAsync(x => x.CountyCode == provider.CountyCode.Value, ct);
            if (row is not null) return row.Region;
        }
        log.LogWarning("Could not resolve region for provider {Name}; defaulting to Downstate", provider.ProviderName);
        return "Downstate";
    }

    // -----------------------------------------------------------------
    // Per-line context
    // -----------------------------------------------------------------

    private class LineContext
    {
        public required ServiceLineDto Svc { get; init; }
        public int? Eapg;
        public string? EapgDesc;
        public EapgType EapgType = EapgType.Unknown;
        public string? EapgTypeRaw;
        public string? EapgCategory;
        public decimal? Weight;
        public decimal RawPayment;
        public bool Packaged;
        public bool Discounted;
        public bool U6Applied;
        public bool Denied;
        public bool FeeScheduled;
        public bool PxWeightApplied;
        public List<string> Notes = new();
    }

    // -----------------------------------------------------------------
    // calculate
    // -----------------------------------------------------------------

    public async Task<APGResult> CalculateAsync(
        ParsedClaimDto claim, ProviderConfig provider, CancellationToken ct = default)
    {
        if (!claim.DateOfService.HasValue)
        {
            return ZeroResult(claim, provider, "Downstate", 0,
                "No date of service on claim; cannot calculate APG payment.");
        }
        var dos = claim.DateOfService.Value;
        var region = await ResolveRegionAsync(provider, ct);
        var baseRateRow = await LookupBaseRateAsync(provider.ProviderType, provider.PeerGroup, region, dos, ct);
        var baseRate = baseRateRow?.Rate ?? 0m;

        var notes = new List<string>();
        if (baseRateRow is null)
        {
            notes.Add(
                $"No base rate found for {provider.ProviderType}/{provider.PeerGroup}/{region} "
              + $"on or before {dos:yyyy-MM-dd}.");
        }

        // Step 1: build per-line context with EAPG assignments and the priority ladder
        var contexts = new List<LineContext>();
        foreach (var sl in claim.ServiceLines)
        {
            var ctx = new LineContext { Svc = sl };
            var code = sl.ProcedureCode ?? "";

            // Priority #1: Fee Schedule (flat × units)
            if (!string.IsNullOrEmpty(code))
            {
                var fs = await LookupFeeScheduleAsync(code, dos, ct);
                if (fs is not null && fs.Reimbursement > 0)
                {
                    var units = (decimal)Math.Max(1, sl.Units);
                    if (fs.MaxUnits is > 0) units = Math.Min(units, fs.MaxUnits.Value);
                    ctx.RawPayment = fs.Reimbursement * units;
                    ctx.FeeScheduled = true;
                    ctx.Notes.Add(
                        $"Fee Schedule applied: ${fs.Reimbursement} × {units} units "
                      + $"(eff {fs.EffectiveDate:yyyy-MM-dd}). APG formula bypassed.");
                }
            }

            // HCPCS → EAPG
            if (!string.IsNullOrEmpty(code))
            {
                var hit = await LookupHcpcsAsync(code, dos, ct);
                if (hit is not null)
                {
                    ctx.Eapg = hit.Eapg;
                    ctx.EapgDesc = hit.EapgDesc;
                    ctx.EapgTypeRaw = hit.EapgType;
                    ctx.EapgType = EapgTypeCoercer.Coerce(hit.EapgType);
                    ctx.EapgCategory = hit.EapgCategory;
                }
                else if (!ctx.FeeScheduled)
                {
                    ctx.Notes.Add($"No EAPG mapping for HCPCS {code} on {dos:yyyy-MM-dd}.");
                }
            }

            // Visit-purpose ICD override for Incidental placeholders
            // (the 99213 + E11.9 → real EAPG rule from the Python service)
            if (ctx.EapgType == EapgType.Incidental && !string.IsNullOrEmpty(claim.PrincipalDiagnosis))
            {
                var icd = await LookupIcd10Async(claim.PrincipalDiagnosis, dos, ct);
                if (icd is not null && icd.Eapg != ctx.Eapg)
                {
                    var placeholder = ctx.Eapg;
                    ctx.Notes.Add(
                        $"Visit-purpose adjustment: HCPCS {code} maps to Incidental "
                      + $"placeholder EAPG {placeholder}; using ICD-10 {claim.PrincipalDiagnosis}'s "
                      + $"EAPG {icd.Eapg}"
                      + (string.IsNullOrEmpty(icd.EapgDesc) ? "" : $" ({icd.EapgDesc})")
                      + " instead.");
                    ctx.Eapg = icd.Eapg;
                    ctx.EapgDesc = icd.EapgDesc ?? icd.Description;
                    ctx.EapgTypeRaw = icd.EapgType;
                    ctx.EapgType = EapgTypeCoercer.Coerce(icd.EapgType);
                    ctx.EapgCategory = icd.EapgCategory;
                }
            }

            // ICD fallback when HCPCS yielded nothing
            if (ctx.Eapg is null && !string.IsNullOrEmpty(claim.PrincipalDiagnosis))
            {
                var icd = await LookupIcd10Async(claim.PrincipalDiagnosis, dos, ct);
                if (icd is not null)
                {
                    ctx.Eapg = icd.Eapg;
                    ctx.EapgDesc = icd.EapgDesc;
                    ctx.EapgTypeRaw = icd.EapgType;
                    ctx.EapgType = EapgTypeCoercer.Coerce(icd.EapgType);
                    ctx.EapgCategory = icd.EapgCategory;
                    ctx.Notes.Add(
                        $"Used ICD-10 {claim.PrincipalDiagnosis} for EAPG assignment (no HCPCS hit).");
                }
            }

            // Priority #2: Px-Based Weight override
            if (!ctx.FeeScheduled && !string.IsNullOrEmpty(code))
            {
                var px = await LookupPxWeightAsync(code, dos, ct);
                if (px is not null && px.Weight > 0)
                {
                    ctx.Weight = px.Weight;
                    ctx.PxWeightApplied = true;
                    ctx.Notes.Add(
                        $"Px-Based Weight applied: {px.Weight} (eff {px.EffectiveDate:yyyy-MM-dd}); "
                      + "overrides APG weight.");
                }
            }

            // Priority #3: APG weight (classic)
            if (!ctx.FeeScheduled && !ctx.PxWeightApplied && ctx.Eapg.HasValue)
            {
                var w = await LookupApgWeightAsync(ctx.Eapg.Value, dos, ct);
                if (w is not null)
                {
                    ctx.Weight = w.Weight;
                    if (w.Weight == 0)
                        ctx.Notes.Add($"APG {ctx.Eapg} has weight 0 for this DOS — not separately payable.");
                }
                else
                {
                    ctx.Notes.Add($"No weight history for APG {ctx.Eapg} on {dos:yyyy-MM-dd}.");
                }
            }

            contexts.Add(ctx);
        }

        // Step 2: packaging + discounting + U6
        ApplyPackaging(contexts);
        var discounting = ApplyDiscounting(contexts, baseRate);
        var u6Any = ApplyU6(contexts, baseRate);

        // Step 3: sum line payments
        foreach (var ctx in contexts)
        {
            if (ctx.Packaged || ctx.Denied) { ctx.RawPayment = 0; continue; }
            if (ctx.FeeScheduled) continue;   // already set from priority #1
            if (ctx.RawPayment == 0 && ctx.Weight is > 0)
                ctx.RawPayment = baseRate * ctx.Weight.Value;
        }

        var totalLinePayment = contexts.Sum(c => RoundMoney(c.RawPayment));

        // Step 4: capital add-on
        var capitalApplied = false;
        var capitalAmount = 0m;
        if (provider.CapitalAddonEligible && provider.CapitalAddonRate.HasValue)
        {
            capitalAmount = RoundMoney(provider.CapitalAddonRate.Value);
            capitalApplied = true;
        }

        var correctPayment = RoundMoney(totalLinePayment + capitalAmount);
        var actualPaid = RoundMoney(claim.PaidAmount);
        var variance = RoundMoney(correctPayment - actualPaid);
        var compressionPct = correctPayment != 0
            ? Math.Round(variance / correctPayment * 100m, 4, MidpointRounding.AwayFromZero)
            : 0m;

        var lineResults = contexts.Select(c => new APGLineResult
        {
            LineSeq = c.Svc.LineSeq,
            ProcedureCode = c.Svc.ProcedureCode,
            Modifiers = c.Svc.Modifiers,
            Eapg = c.Eapg,
            EapgDesc = c.EapgDesc,
            EapgType = c.EapgType,
            EapgTypeRaw = c.EapgTypeRaw,
            EapgCategory = c.EapgCategory,
            Weight = c.Weight,
            BaseRate = baseRate,
            ExpectedPayment = RoundMoney(c.RawPayment),
            ActualPaid = RoundMoney(c.Svc.PaidAmount),
            Variance = RoundMoney(c.RawPayment - c.Svc.PaidAmount),
            Packaged = c.Packaged,
            Discounted = c.Discounted,
            U6Applied = c.U6Applied,
            Denied = c.Denied,
            FeeScheduled = c.FeeScheduled,
            PxWeightApplied = c.PxWeightApplied,
            Notes = c.Notes,
        }).ToList();

        return new APGResult
        {
            ClaimId = claim.ClaimId,
            DateOfService = dos,
            PeerGroup = provider.PeerGroup,
            Region = region,
            BaseRateApplied = baseRate,
            CorrectApgPayment = correctPayment,
            ActualPaid = actualPaid,
            Variance = variance,
            CompressionPct = compressionPct,
            Underpaid = variance > 0,
            Overpaid = variance < 0,
            DiscountingApplied = discounting,
            U6Applied = u6Any,
            CapitalApplied = capitalApplied,
            CapitalAddonAmount = capitalAmount,
            LineDetails = lineResults,
            Notes = notes,
        };
    }

    // -----------------------------------------------------------------
    // ICD-derived informational EAPG (Rate Calculator panel)
    // -----------------------------------------------------------------

    public async Task<ICDBasedEAPG?> ResolveIcdBasedEapgAsync(
        string? rawDx, DateOnly dos, decimal baseRate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawDx)) return null;
        var normalized = DxCodeNormalizer.Normalize(rawDx) ?? "";
        var icd = await LookupIcd10Async(normalized, dos, ct);

        if (icd is null)
        {
            return new ICDBasedEAPG
            {
                DxCode = normalized,
                InputDxCode = rawDx,
                BaseRate = baseRate,
                Note = $"ICD-10 '{rawDx}' did not resolve to an EAPG on {dos:yyyy-MM-dd}.",
            };
        }

        var weightRow = await LookupApgWeightAsync(icd.Eapg, dos, ct);
        var weight = weightRow?.Weight;
        decimal? indicative = (weight is > 0 && baseRate > 0) ? RoundMoney(weight.Value * baseRate) : null;

        return new ICDBasedEAPG
        {
            DxCode = normalized,
            InputDxCode = rawDx,
            Eapg = icd.Eapg,
            EapgDesc = icd.EapgDesc ?? icd.Description,
            EapgType = EapgTypeCoercer.Coerce(icd.EapgType),
            EapgTypeRaw = icd.EapgType,
            EapgCategory = icd.EapgCategory,
            Weight = weight,
            BaseRate = baseRate,
            IndicativePayment = indicative,
            Note = weight.HasValue ? null
                : $"No weight row found for EAPG {icd.Eapg} on {dos:yyyy-MM-dd} — indicative rate unavailable.",
        };
    }

    // -----------------------------------------------------------------
    // Rule helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Incidental EAPGs are always packaged. If the claim has at least one
    /// (non-fee-scheduled) Significant Procedure with weight &gt; 0, Medical
    /// Visit lines also package per NYS DOH packaging policy. Zero-weight
    /// lines package because weight 0 means "not separately payable."
    /// </summary>
    private static void ApplyPackaging(List<LineContext> contexts)
    {
        var hasSignificant = contexts.Any(c =>
            c.EapgType == EapgType.SignificantProcedure
            && (c.Weight ?? 0m) > 0
            && !c.FeeScheduled);

        foreach (var c in contexts)
        {
            if (c.FeeScheduled) continue;   // pays at flat rate; never packages

            if (c.EapgType == EapgType.Incidental)
            {
                c.Packaged = true;
                c.Notes.Add("Packaged: Incidental EAPG type is not separately payable.");
                continue;
            }
            if (c.Weight is 0m)
            {
                c.Packaged = true;
                c.Notes.Add("Packaged: weight is 0 for this date of service.");
                continue;
            }
            if (c.EapgType == EapgType.MedicalVisit && hasSignificant)
            {
                c.Packaged = true;
                c.Notes.Add("Packaged: Medical Visit bundled into Significant Procedure on same claim.");
            }
        }
    }

    /// <summary>
    /// Rank payable Significant-Procedure lines by weight DESC. Primary
    /// pays 100%; all others pay MultiProcedureDiscount (50%).
    /// </summary>
    private static bool ApplyDiscounting(List<LineContext> contexts, decimal baseRate)
    {
        var sigLines = contexts
            .Where(c => c.EapgType == EapgType.SignificantProcedure
                     && !c.Packaged
                     && !c.FeeScheduled
                     && c.Weight is > 0)
            .OrderByDescending(c => c.Weight ?? 0m)
            .ToList();
        if (sigLines.Count == 0) return false;

        bool discountingApplied = false;
        for (int i = 0; i < sigLines.Count; i++)
        {
            var c = sigLines[i];
            if (i == 0)
            {
                c.RawPayment = baseRate * c.Weight!.Value;
            }
            else
            {
                c.RawPayment = baseRate * c.Weight!.Value * MultiProcedureDiscount;
                c.Discounted = true;
                discountingApplied = true;
                c.Notes.Add("Discounted to 50% as secondary significant procedure.");
            }
        }
        return discountingApplied;
    }

    /// <summary>
    /// If any modifier on a line contains 'U6', apply U6AdjustmentFactor.
    /// </summary>
    private static bool ApplyU6(List<LineContext> contexts, decimal baseRate)
    {
        bool any = false;
        foreach (var c in contexts)
        {
            if (!c.Svc.Modifiers.Any(m => m?.Equals("U6", StringComparison.OrdinalIgnoreCase) == true)) continue;
            any = true;
            c.U6Applied = true;
            if (c.RawPayment > 0)
                c.RawPayment *= U6AdjustmentFactor;
            else if (c.Weight is > 0 && !c.Packaged)
                c.RawPayment = baseRate * c.Weight.Value * U6AdjustmentFactor;
            c.Notes.Add($"Modifier U6 applied: rate × {U6AdjustmentFactor}.");
        }
        return any;
    }

    private static APGResult ZeroResult(
        ParsedClaimDto claim, ProviderConfig provider, string region, decimal baseRate, string note)
    {
        return new APGResult
        {
            ClaimId = claim.ClaimId,
            DateOfService = claim.DateOfService,
            PeerGroup = provider.PeerGroup,
            Region = region,
            BaseRateApplied = baseRate,
            CorrectApgPayment = 0,
            ActualPaid = RoundMoney(claim.PaidAmount),
            Variance = -RoundMoney(claim.PaidAmount),
            CompressionPct = 0,
            Underpaid = false,
            Overpaid = claim.PaidAmount > 0,
            Notes = new List<string> { note },
        };
    }

    private static decimal RoundMoney(decimal v)
        => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
