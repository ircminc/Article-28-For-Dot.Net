using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;

namespace APGAnalyzer.Services;

public interface IApgEngine
{
    Task<APGResult> CalculateAsync(
        ParsedClaimDto claim,
        ProviderConfig provider,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the principal ICD to its EAPG (informational, used by the
    /// Rate Calculator's "Primary ICD-derived EAPG" panel).
    /// </summary>
    Task<ICDBasedEAPG?> ResolveIcdBasedEapgAsync(
        string? rawDx,
        DateOnly dos,
        decimal baseRate,
        CancellationToken ct = default);
}
