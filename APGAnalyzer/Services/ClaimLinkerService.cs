using System.Text.Json;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

public interface IClaimLinkerService
{
    Task<ClaimLinkerResult> LinkAndEnrichAsync(
        IEnumerable<string> claimIds,
        ProviderConfig provider,
        string ownerUserId,
        CancellationToken ct = default);
}

public class ClaimLinkerResult
{
    public int LinkedPairs { get; set; }
    public int ApgRecalcs { get; set; }
}

/// <summary>
/// Auto-link 837 submissions with matching 835 remittances.
///
/// A single claim has two EDI representations:
///   * The 837 (submission) — diagnoses, charges, modifiers
///   * The 835 (remit)      — paid/allowed, adjustments, CARC reasons
///
/// When both land in our database for the same CLM01/CLP01 ID, we:
///   1. Set LinkedClaimIdFk on both records (one-shot bidirectional link)
///   2. Copy principal_diagnosis + other_diagnoses from 837 → 835 if missing,
///      because the engine uses dx codes for the visit-purpose override
///      (the 99213+E11.9 → $132.09 rule)
///   3. Re-run the APG engine on the 835 with the enriched data
///
/// Direct port of backend/engines/claim_linker.py.
/// </summary>
public class ClaimLinkerService(
    ApplicationDbContext db,
    IApgEngine engine,
    ILogger<ClaimLinkerService> log) : IClaimLinkerService
{
    private static readonly HashSet<string> EraTypes =
        new(StringComparer.OrdinalIgnoreCase) { "835I", "835P" };
    private static readonly HashSet<string> SubmissionTypes =
        new(StringComparer.OrdinalIgnoreCase) { "837I", "837P" };

    public async Task<ClaimLinkerResult> LinkAndEnrichAsync(
        IEnumerable<string> claimIds, ProviderConfig provider,
        string ownerUserId, CancellationToken ct = default)
    {
        var result = new ClaimLinkerResult();
        var distinctIds = claimIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();
        if (distinctIds.Count == 0) return result;

        // Pull all DB rows matching these claim IDs (across both 837 and 835).
        // Scoped to the uploader so two analysts who happen to upload claims
        // with the same CLM01 don't accidentally cross-link.
        var rows = await db.ParsedClaims
            .Include(c => c.ServiceLines)
            .Include(c => c.ApgResult)
            .Where(c => c.OwnerUserId == ownerUserId && distinctIds.Contains(c.ClaimId))
            .ToListAsync(ct);

        var byId = rows.GroupBy(r => r.ClaimId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (cid, group) in byId)
        {
            var eras = group.Where(c => EraTypes.Contains(c.FileType)).ToList();
            var subs = group.Where(c => SubmissionTypes.Contains(c.FileType)).ToList();
            if (eras.Count == 0 || subs.Count == 0) continue;

            // Prefer the most recent of each type (multiple uploads possible)
            var era = eras.OrderByDescending(c => c.CreatedAt).First();
            var sub = subs.OrderByDescending(c => c.CreatedAt).First();

            // Bidirectional link
            era.LinkedClaimIdFk = sub.Id;
            sub.LinkedClaimIdFk = era.Id;
            result.LinkedPairs++;

            // Enrich era with sub's dx codes if missing
            bool enriched = false;
            if (!string.IsNullOrEmpty(sub.PrincipalDiagnosis)
                && string.IsNullOrEmpty(era.PrincipalDiagnosis))
            {
                era.PrincipalDiagnosis = sub.PrincipalDiagnosis;
                enriched = true;
            }

            // Merge other-dx lists (dedupe, preserve order)
            var subOthers = string.IsNullOrEmpty(sub.OtherDiagnosesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(sub.OtherDiagnosesJson) ?? new();
            var eraOthers = string.IsNullOrEmpty(era.OtherDiagnosesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(era.OtherDiagnosesJson) ?? new();
            var merged = eraOthers.Concat(subOthers).Distinct().ToList();
            if (!merged.SequenceEqual(eraOthers))
            {
                era.OtherDiagnosesJson = merged.Count == 0 ? null : JsonSerializer.Serialize(merged);
                enriched = true;
            }

            if (!enriched) continue;

            // Re-run APG against the enriched era claim
            try
            {
                var dto = ClaimUploadService.ToEngineDto(era);
                var apgResult = await engine.CalculateAsync(dto, provider, ct);

                if (era.ApgResult is null)
                {
                    db.ApgResults.Add(new ApgResultRecord
                    {
                        ClaimIdFk = era.Id,
                        CorrectApgPayment = apgResult.CorrectApgPayment,
                        ActualPaid = apgResult.ActualPaid,
                        Variance = apgResult.Variance,
                        CompressionPct = apgResult.CompressionPct,
                        Underpaid = apgResult.Underpaid,
                        Overpaid = apgResult.Overpaid,
                        BaseRateApplied = apgResult.BaseRateApplied,
                        PeerGroup = apgResult.PeerGroup,
                        Region = apgResult.Region,
                        DiscountingApplied = apgResult.DiscountingApplied,
                        U6Applied = apgResult.U6Applied,
                        CapitalApplied = apgResult.CapitalApplied,
                        LineDetailsJson = JsonSerializer.Serialize(apgResult.LineDetails),
                        CalculatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    var r = era.ApgResult;
                    r.CorrectApgPayment   = apgResult.CorrectApgPayment;
                    r.ActualPaid          = apgResult.ActualPaid;
                    r.Variance            = apgResult.Variance;
                    r.CompressionPct      = apgResult.CompressionPct;
                    r.Underpaid           = apgResult.Underpaid;
                    r.Overpaid            = apgResult.Overpaid;
                    r.BaseRateApplied     = apgResult.BaseRateApplied;
                    r.PeerGroup           = apgResult.PeerGroup;
                    r.Region              = apgResult.Region;
                    r.DiscountingApplied  = apgResult.DiscountingApplied;
                    r.U6Applied           = apgResult.U6Applied;
                    r.CapitalApplied      = apgResult.CapitalApplied;
                    r.LineDetailsJson     = JsonSerializer.Serialize(apgResult.LineDetails);
                    r.CalculatedAt        = DateTime.UtcNow;
                }
                result.ApgRecalcs++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex,
                    "Claim linker: APG re-calc failed for claim {ClaimId} after dx enrichment", cid);
            }
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Claim linker: {Pairs} pair(s) linked, {Recalcs} APG re-calc(s)",
            result.LinkedPairs, result.ApgRecalcs);
        return result;
    }
}
