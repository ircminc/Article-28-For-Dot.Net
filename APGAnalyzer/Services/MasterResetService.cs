using System.Diagnostics;
using APGAnalyzer.Data;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

public interface IMasterResetService
{
    Task<MasterResetResult> ResetAsync(CancellationToken ct = default);
}

/// <summary>
/// Wipes every reference / rate table so a fresh upload can start clean.
/// Preserves users, audit log, providers (when added), and county/locality
/// tables.
///
/// Mirrors the DELETE /api/admin/master-reset-reference-data endpoint in
/// the Python service. Two-step UI confirmation lives in the controller +
/// view; this service is the underlying transaction.
/// </summary>
public class MasterResetService(ApplicationDbContext db, ILogger<MasterResetService> log)
    : IMasterResetService
{
    public async Task<MasterResetResult> ResetAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new MasterResetResult
        {
            PreservedTables = new[]
            {
                "AspNetUsers", "AspNetRoles", "AspNetUserRoles",
                "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens",
                "AspNetRoleClaims",
                // Once added in later phases:
                // "audit_log", "provider_config", "provider_county",
                // "zip_locality", "cms_rate_cache", "parsed_claim",
                // "parsed_service_line", "claim_adjustment", "apg_result",
            },
        };

        // Order doesn't matter — these tables have no FKs between them.
        // Run each ExecuteDelete in turn; each is a single bulk DELETE
        // round-trip, so 7 deletes is 7 cheap statements.
        result.ByTable["hcpcs_to_eapg"]    = await db.HcpcsToEapg.ExecuteDeleteAsync(ct);
        result.ByTable["icd10_to_eapg"]    = await db.Icd10ToEapg.ExecuteDeleteAsync(ct);
        result.ByTable["apg_weights"]      = await db.ApgWeights.ExecuteDeleteAsync(ct);
        result.ByTable["apg_base_rates"]   = await db.ApgBaseRates.ExecuteDeleteAsync(ct);
        result.ByTable["px_based_weights"] = await db.PxBasedWeights.ExecuteDeleteAsync(ct);
        result.ByTable["fee_schedule"]     = await db.FeeSchedule.ExecuteDeleteAsync(ct);
        result.ByTable["provider_county"]  = await db.ProviderCounties.ExecuteDeleteAsync(ct);

        result.RowsDeletedTotal = result.ByTable.Values.Sum();
        result.Elapsed = stopwatch.Elapsed;

        log.LogWarning(
            "Master Reset: {Total} rows removed across {Tables} tables in {Elapsed:F2}s",
            result.RowsDeletedTotal, result.ByTable.Count, result.Elapsed.TotalSeconds);

        return result;
    }
}
