using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using APGAnalyzer.Data;
using APGAnalyzer.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services.Cms;

/// <summary>
/// CMS Medicare Physician Fee Schedule (MPFS) rate engine.
///
/// Direct port of <c>backend/engines/cms_engine.py</c>. Mirrors the official
/// CMS PFS Look-Up Tool formula:
///
///   payment = ((rvu_work × gpci_work)
///            + (pe_rvu    × gpci_pe)
///            + (rvu_mp    × gpci_mp)) × conversion_factor
///
/// Data source: <c>https://pfs.data.cms.gov</c> (DKAN datastore). Catalog at
/// <c>/data.json</c>; per-dataset POST queries at
/// <c>/api/1/datastore/query/{uuid}/0</c>. Two datasets per year:
/// "Indicators for YYYY" (RVUs + CF + procedure flags) and
/// "Localities for YYYY" (GPCIs per Medicare locality). UUIDs are NEVER
/// hardcoded — they're discovered at runtime from the catalog and matched
/// by title regex. When CMS publishes a new annual dataset, the engine
/// picks it up on the next 24h catalog refresh.
///
/// Caches:
///   * In-process catalog cache: 24h TTL, keyed by year
///   * In-process locality-list cache: 24h TTL, keyed by year (~110 rows)
///   * Persistent DB cache: <see cref="CmsRateCache"/> table, 24h TTL,
///     graceful stale-cache fallback when CMS is unreachable
/// </summary>
public interface ICmsRateService
{
    /// <summary>
    /// Look up an MPFS rate. Cache fast-path; on miss hits CMS, computes,
    /// caches the result. Returns null when CMS has no row for the inputs
    /// (and we have no stale cache to fall back on). Throws
    /// <see cref="CmsDatasetMovedException"/> when the catalog itself is
    /// unreachable — callers should surface that as a banner.
    /// </summary>
    Task<CmsRateCache?> GetMpfsRateAsync(
        string hcpcs, string modifier, string locality, int year,
        bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>
    /// List every CMS Medicare locality for the requested year. Cached
    /// in-process for 24h since UI dropdowns hit it on every page load.
    /// Returns an empty list (not throw) on catalog failure so the UI
    /// degrades gracefully.
    /// </summary>
    Task<IReadOnlyList<CmsLocality>> ListLocalitiesAsync(
        int year, CancellationToken ct = default);

    /// <summary>
    /// Force the engine to forget everything it has cached and re-discover
    /// from CMS on the next lookup. Used when CMS publishes a quarterly
    /// fee schedule update and the admin wants to make sure subsequent
    /// rate calculations pull fresh data.
    ///
    /// Implementation:
    ///   1. Clear in-process catalog cache (so dataset UUIDs are re-discovered —
    ///      important when CMS publishes "Indicators for YYYY-B" mid-year)
    ///   2. Clear in-process locality-list cache
    ///   3. Mark every <see cref="CmsRateCache"/> row stale (CachedUntil = utcnow)
    ///      so the very next lookup for that HCPCS/locality pulls fresh data.
    ///
    /// We don't pre-fetch rates eagerly: the cache rebuilds lazily as users
    /// query, which keeps this action instant and avoids hammering CMS.
    /// </summary>
    Task<CmsCacheRefreshResult> RefreshCacheAsync(CancellationToken ct = default);
}

/// <summary>Outcome of an admin-driven CMS cache refresh.</summary>
public class CmsCacheRefreshResult
{
    public int RowsMarkedStale { get; set; }
    public int CatalogYearsCleared { get; set; }
    public int LocalityYearsCleared { get; set; }
    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CmsLocality
{
    public string Locality { get; set; } = "";        // "1320201"
    public string Description { get; set; } = "";     // "MANHATTAN"
    public string Mac { get; set; } = "";             // "13202"
    public string MacDescription { get; set; } = "";  // "NATIONAL" / "EMPIRE BLUE CROSS"
}

public class CmsDatasetMovedException : Exception
{
    public CmsDatasetMovedException(string message) : base(message) { }
}

/// <inheritdoc cref="ICmsRateService"/>
public class CmsRateService : ICmsRateService
{
    private const string CmsBase    = "https://pfs.data.cms.gov";
    private const string CatalogUrl = CmsBase + "/data.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private static readonly Regex YearSuffixRe =
        new(@"\bfor\s+(\d{4})([AB]?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UuidRe =
        new(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CmsRateService> _log;

    // In-process caches (per-process, reset on app restart).
    // Key = year, Value = (indicatorUuid, localityUuid, fetchedAt)
    private static readonly Dictionary<int, (string Ind, string Loc, DateTime At)> _catalogCache = new();
    private static readonly SemaphoreSlim _catalogLock = new(1, 1);
    // Key = year, Value = (rows, fetchedAt)
    private static readonly Dictionary<int, (List<CmsLocality> Rows, DateTime At)> _localityListCache = new();
    private static readonly SemaphoreSlim _localityListLock = new(1, 1);

    public CmsRateService(
        HttpClient http,
        ApplicationDbContext db,
        ILogger<CmsRateService> log)
    {
        _http = http;
        _db = db;
        _log = log;
        // Defensive: enforce a sensible per-request timeout regardless of HttpClient defaults.
        if (_http.Timeout == Timeout.InfiniteTimeSpan)
            _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    public async Task<CmsRateCache?> GetMpfsRateAsync(
        string hcpcs, string modifier, string locality, int year,
        bool forceRefresh = false, CancellationToken ct = default)
    {
        hcpcs    = (hcpcs ?? "").Trim().ToUpperInvariant();
        modifier = (modifier ?? "").Trim().ToUpperInvariant();
        locality = NormalizeLocality(locality);

        // 1. DB cache fast path (skipped on forceRefresh)
        var cached = await _db.CmsRateCache.FirstOrDefaultAsync(
            x => x.Hcpcs == hcpcs && x.Modifier == modifier
              && x.Locality == locality && x.Year == year, ct);

        var now = DateTime.UtcNow;
        if (cached is not null && !forceRefresh && cached.CachedUntil > now)
            return cached;

        // 2. Live fetch + compute
        ComputedRate? parsed;
        try
        {
            parsed = await ComputeRateAsync(hcpcs, modifier, locality, year, ct);
        }
        catch (CmsDatasetMovedException)
        {
            // Bubble — the calculator UI shows this as a banner.
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "CMS rate fetch failed for {Hcpcs}/{Mod}/{Loc}/{Year}",
                hcpcs, modifier, locality, year);
            parsed = null;
        }

        // 3. Stale-cache fallback if live fetch failed
        if (parsed is null)
        {
            if (cached is not null)
            {
                _log.LogInformation(
                    "CMS unreachable; returning stale cache for {Hcpcs}/{Mod}/{Loc}/{Year}",
                    hcpcs, modifier, locality, year);
                return cached;
            }
            return null;
        }

        // 4. Cache put
        if (cached is null)
        {
            cached = new CmsRateCache
            {
                Hcpcs    = hcpcs,
                Modifier = modifier,
                Locality = locality,
                Year     = year,
            };
            _db.CmsRateCache.Add(cached);
        }
        cached.NonFacilityRate  = parsed.NonFacilityRate;
        cached.FacilityRate     = parsed.FacilityRate;
        cached.WorkRvu          = parsed.WorkRvu;
        cached.PeRvu            = parsed.PeRvu;
        cached.MpRvu            = parsed.MpRvu;
        cached.TotalRvu         = parsed.TotalRvu;
        cached.ConversionFactor = parsed.ConversionFactor;
        cached.RawPayloadJson   = parsed.RawPayloadJson;
        cached.CachedAt         = now;
        cached.CachedUntil      = now + CacheTtl;
        await _db.SaveChangesAsync(ct);
        return cached;
    }

    public async Task<IReadOnlyList<CmsLocality>> ListLocalitiesAsync(
        int year, CancellationToken ct = default)
    {
        // In-process cache fast path
        await _localityListLock.WaitAsync(ct);
        try
        {
            if (_localityListCache.TryGetValue(year, out var hit)
                && DateTime.UtcNow - hit.At < CacheTtl)
            {
                return hit.Rows;
            }
        }
        finally { _localityListLock.Release(); }

        // Live fetch
        string locUuid;
        try
        {
            (_, locUuid) = await ResolveDatasetsForYearAsync(year, ct);
        }
        catch (CmsDatasetMovedException ex)
        {
            _log.LogWarning(ex, "CMS catalog unreachable while listing localities for {Year}", year);
            return Array.Empty<CmsLocality>();
        }

        List<JsonElement> raw;
        try
        {
            raw = await DkanQueryAsync(locUuid, new(), limit: 500, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CMS locality dataset fetch failed for {Year}", year);
            return Array.Empty<CmsLocality>();
        }

        var localities = new List<CmsLocality>(raw.Count);
        foreach (var r in raw)
        {
            var loc = ReadString(r, "locality");
            if (string.IsNullOrWhiteSpace(loc)) continue;
            localities.Add(new CmsLocality
            {
                Locality       = loc.Trim(),
                Description    = ReadString(r, "loc_description").Trim(),
                Mac            = ReadString(r, "mac").Trim(),
                MacDescription = ReadString(r, "mac_description").Trim(),
            });
        }

        // National first, then by MAC region, then by description.
        localities.Sort((a, b) =>
        {
            int aNat = string.Equals(a.MacDescription, "NATIONAL", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int bNat = string.Equals(b.MacDescription, "NATIONAL", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int c = aNat.CompareTo(bNat);
            if (c != 0) return c;
            c = string.Compare(a.MacDescription, b.MacDescription, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase);
        });

        await _localityListLock.WaitAsync(ct);
        try { _localityListCache[year] = (localities, DateTime.UtcNow); }
        finally { _localityListLock.Release(); }

        return localities;
    }

    public async Task<CmsCacheRefreshResult> RefreshCacheAsync(CancellationToken ct = default)
    {
        var result = new CmsCacheRefreshResult();

        // 1. Clear in-process catalog cache (dataset UUIDs re-discovered next request).
        await _catalogLock.WaitAsync(ct);
        try
        {
            result.CatalogYearsCleared = _catalogCache.Count;
            _catalogCache.Clear();
        }
        finally { _catalogLock.Release(); }

        // 2. Clear in-process locality-list cache.
        await _localityListLock.WaitAsync(ct);
        try
        {
            result.LocalityYearsCleared = _localityListCache.Count;
            _localityListCache.Clear();
        }
        finally { _localityListLock.Release(); }

        // 3. Mark every cached rate row stale (next lookup → re-fetch).
        //    ExecuteUpdate is a single SQL UPDATE — fast even with thousands
        //    of rows, no row materialization.
        var nowUtc = DateTime.UtcNow;
        result.RowsMarkedStale = await _db.CmsRateCache
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CachedUntil, nowUtc), ct);

        _log.LogInformation(
            "CMS cache refresh: cleared {CatYears} catalog year(s), "
          + "{LocYears} locality year(s), marked {Stale} DB row(s) stale",
            result.CatalogYearsCleared, result.LocalityYearsCleared, result.RowsMarkedStale);

        return result;
    }

    // -----------------------------------------------------------------
    // Catalog discovery (UUID resolution)
    // -----------------------------------------------------------------

    private async Task<(string IndUuid, string LocUuid)> ResolveDatasetsForYearAsync(
        int year, CancellationToken ct)
    {
        // Cache fast path
        if (_catalogCache.TryGetValue(year, out var hit)
            && DateTime.UtcNow - hit.At < CacheTtl)
        {
            return (hit.Ind, hit.Loc);
        }

        await _catalogLock.WaitAsync(ct);
        try
        {
            if (_catalogCache.TryGetValue(year, out hit)
                && DateTime.UtcNow - hit.At < CacheTtl)
            {
                return (hit.Ind, hit.Loc);
            }

            var datasets = await FetchCatalogAsync(ct);

            // Index by (year, suffix), kind = indicator | locality
            var indicators = new Dictionary<(int Y, string Sfx), string>();
            var localities = new Dictionary<(int Y, string Sfx), string>();

            foreach (var ds in datasets)
            {
                var title = ReadString(ds, "title");
                var ident = ReadString(ds, "identifier");
                if (string.IsNullOrEmpty(ident)) ident = ReadString(ds, "id");
                if (string.IsNullOrEmpty(ident)) continue;

                var um = UuidRe.Match(ident);
                if (!um.Success) continue;
                var uuid = um.Groups[1].Value.ToLowerInvariant();

                var ys = ParseYearSuffix(title);
                if (ys is null) continue;
                var (y, sfx) = ys.Value;

                var tlow = title.ToLowerInvariant();
                if (tlow.StartsWith("indicators for")) indicators[(y, sfx)] = uuid;
                else if (tlow.StartsWith("localities for")) localities[(y, sfx)] = uuid;
            }

            _log.LogInformation(
                "CMS catalog: parsed {IndCount} indicator + {LocCount} locality dataset(s)",
                indicators.Count, localities.Count);

            string? indUuid = Pick(indicators, year);
            string? locUuid = Pick(localities, year);

            // Fall back to most recent prior year if needed (CMS sometimes
            // publishes new-year datasets weeks after Jan 1).
            if (indUuid is null || locUuid is null)
            {
                var candidateYears = indicators.Keys.Select(k => k.Y)
                    .Concat(localities.Keys.Select(k => k.Y))
                    .Distinct()
                    .OrderByDescending(y => y);
                foreach (var cy in candidateYears)
                {
                    if (cy > year) continue;
                    indUuid ??= Pick(indicators, cy);
                    locUuid ??= Pick(localities, cy);
                    if (indUuid is not null && locUuid is not null)
                    {
                        if (cy != year)
                            _log.LogInformation("CMS: no dataset for {Year}; falling back to {Cy}", year, cy);
                        break;
                    }
                }
            }

            if (indUuid is null || locUuid is null)
                throw new CmsDatasetMovedException(
                    $"Could not find CMS PFS datasets for year {year} in catalog "
                  + $"(catalog had {indicators.Count} indicator(s), "
                  + $"{localities.Count} locality(ies)).");

            _catalogCache[year] = (indUuid, locUuid, DateTime.UtcNow);
            return (indUuid, locUuid);
        }
        finally { _catalogLock.Release(); }

        static string? Pick(Dictionary<(int Y, string Sfx), string> pool, int forYear)
        {
            // Preference: '' (non-split) > 'B' (H2 update) > 'A' (H1)
            foreach (var sfx in new[] { "", "B", "A" })
                if (pool.TryGetValue((forYear, sfx), out var u)) return u;
            return null;
        }
    }

    private async Task<List<JsonElement>> FetchCatalogAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync(CatalogUrl, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new CmsDatasetMovedException(
                $"CMS catalog at {CatalogUrl} returned 404 — endpoint may have moved.");
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // The catalog is shaped { "dataset": [ ... ] } per data.json conventions,
        // but defensively support a bare array too.
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("dataset", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            return arr.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        throw new CmsDatasetMovedException($"Unexpected catalog shape from {CatalogUrl}");
    }

    // -----------------------------------------------------------------
    // DKAN POST query
    // -----------------------------------------------------------------

    private async Task<List<JsonElement>> DkanQueryAsync(
        string uuid, Dictionary<string, string> filters, int limit, CancellationToken ct)
    {
        var url = $"{CmsBase}/api/1/datastore/query/{uuid}/0";

        var conditions = filters
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => new DkanCondition
            {
                Resource = "t",
                Property = kv.Key,
                Value    = kv.Value,
                Operator = "=",
            })
            .ToList();
        var payload = new DkanQueryPayload { Conditions = conditions, Limit = limit };

        using var resp = await _http.PostAsJsonAsync(url, payload, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new CmsDatasetMovedException($"CMS dataset {uuid} returned 404 — catalog may be stale.");
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("CMS DKAN query returned {Status}: {Body}",
                (int)resp.StatusCode, body.Length > 200 ? body[..200] : body);
            return new();
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("results", out var rs)
            && rs.ValueKind == JsonValueKind.Array)
        {
            return rs.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        return new();
    }

    // -----------------------------------------------------------------
    // Rate computation
    // -----------------------------------------------------------------

    private async Task<ComputedRate?> ComputeRateAsync(
        string hcpcs, string modifier, string locality, int year, CancellationToken ct)
    {
        var (indUuid, locUuid) = await ResolveDatasetsForYearAsync(year, ct);

        // Parallel fetches: HCPCS row + locality row
        var indTask = DkanQueryAsync(indUuid,
            new() { ["hcpc"] = hcpcs, ["modifier"] = modifier }, limit: 2, ct);
        var locTask = DkanQueryAsync(locUuid,
            new() { ["locality"] = locality }, limit: 2, ct);

        List<JsonElement> indRows, locRows;
        try
        {
            await Task.WhenAll(indTask, locTask);
            indRows = await indTask;
            locRows = await locTask;
        }
        catch (CmsDatasetMovedException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CMS DKAN fetch failed");
            return null;
        }

        if (indRows.Count == 0)
        {
            _log.LogInformation("CMS: no Indicators row for {Hcpcs}/{Mod}/{Year}", hcpcs, modifier, year);
            return null;
        }
        if (locRows.Count == 0)
        {
            _log.LogInformation("CMS: no Localities row for {Loc}/{Year}", locality, year);
            return null;
        }

        var ind = indRows[0];
        var loc = locRows[0];

        var rvuWork    = ReadDecimal(ind, "rvu_work")  ?? 0m;
        var fullNfacPe = ReadDecimal(ind, "full_nfac_pe") ?? ReadDecimal(ind, "nfac_pe") ?? 0m;
        var fullFacPe  = ReadDecimal(ind, "full_fac_pe")  ?? ReadDecimal(ind, "fac_pe")  ?? 0m;
        var rvuMp      = ReadDecimal(ind, "rvu_mp") ?? 0m;
        var cf         = ReadDecimal(ind, "conv_fact");

        var gpciWork = ReadDecimal(loc, "gpci_work") ?? 1m;
        var gpciPe   = ReadDecimal(loc, "gpci_pe")   ?? 1m;
        var gpciMp   = ReadDecimal(loc, "gpci_mp")   ?? 1m;

        if (cf is null || cf == 0m)
        {
            _log.LogInformation("CMS: conversion factor missing/zero for {Hcpcs}/{Year}", hcpcs, year);
            return null;
        }

        var workComp     = rvuWork    * gpciWork;
        var mpComp       = rvuMp      * gpciMp;
        var peNfacComp   = fullNfacPe * gpciPe;
        var peFacComp    = fullFacPe  * gpciPe;

        var nonFac = (workComp + peNfacComp + mpComp) * cf.Value;
        var fac    = (workComp + peFacComp  + mpComp) * cf.Value;

        // Round to cents
        nonFac = Math.Round(nonFac, 2, MidpointRounding.AwayFromZero);
        fac    = Math.Round(fac,    2, MidpointRounding.AwayFromZero);

        var totalRvuNfac = rvuWork + fullNfacPe + rvuMp;

        // Stash full audit payload — useful when CMS-vs-paid disagreements need debugging
        var rawPayload = JsonSerializer.Serialize(new
        {
            indicator_row = ind,
            locality_row  = loc,
            computation = new
            {
                work_component     = workComp.ToString(),
                pe_nfac_component  = peNfacComp.ToString(),
                pe_fac_component   = peFacComp.ToString(),
                mp_component       = mpComp.ToString(),
            }
        });

        return new ComputedRate
        {
            NonFacilityRate  = nonFac,
            FacilityRate     = fac,
            WorkRvu          = rvuWork,
            PeRvu            = fullNfacPe,
            MpRvu            = rvuMp,
            TotalRvu         = totalRvuNfac,
            ConversionFactor = cf.Value,
            RawPayloadJson   = rawPayload,
        };
    }

    private record ComputedRate
    {
        public decimal NonFacilityRate { get; init; }
        public decimal FacilityRate { get; init; }
        public decimal WorkRvu { get; init; }
        public decimal PeRvu { get; init; }
        public decimal MpRvu { get; init; }
        public decimal TotalRvu { get; init; }
        public decimal ConversionFactor { get; init; }
        public string RawPayloadJson { get; init; } = "{}";
    }

    // -----------------------------------------------------------------
    // Static helpers
    // -----------------------------------------------------------------

    private static (int Year, string Suffix)? ParseYearSuffix(string title)
    {
        var m = YearSuffixRe.Match(title ?? "");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), m.Groups[2].Value.ToUpperInvariant());
    }

    private static string NormalizeLocality(string code) =>
        string.IsNullOrEmpty(code) ? "" : code.Trim();

    private static string ReadString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(prop, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
    }

    private static decimal? ReadDecimal(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(prop, out var v)) return null;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.TryGetDecimal(out var d) ? d : null;
            case JsonValueKind.String:
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s)) return null;
                return decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var dec)
                    ? dec : null;
            default:
                return null;
        }
    }

    // -----------------------------------------------------------------
    // DKAN payload shapes
    // -----------------------------------------------------------------

    private class DkanQueryPayload
    {
        [JsonPropertyName("conditions")]
        public List<DkanCondition> Conditions { get; set; } = new();
        [JsonPropertyName("limit")]
        public int Limit { get; set; }
    }

    private class DkanCondition
    {
        [JsonPropertyName("resource")] public string Resource { get; set; } = "t";
        [JsonPropertyName("property")] public string Property { get; set; } = "";
        [JsonPropertyName("value")]    public string Value    { get; set; } = "";
        [JsonPropertyName("operator")] public string Operator { get; set; } = "=";
    }
}
