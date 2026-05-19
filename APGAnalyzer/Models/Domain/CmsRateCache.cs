using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Cache of CMS Medicare Physician Fee Schedule (MPFS) lookups. Keyed by
/// (HCPCS, modifier, locality, year). Stale rows (CachedUntil &lt; now)
/// are still returned if the live CMS API is unreachable — graceful
/// degradation matches the Python build.
///
/// Reference data, NOT user-owned. Every analyst's CMS lookups share the
/// same cache to avoid hammering the CMS API.
///
/// Mirrors backend/db/database.py:CmsRateCache in the Python service.
/// </summary>
[Table("cms_rate_cache")]
public class CmsRateCache
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(12)]
    public string Hcpcs { get; set; } = "";

    [Required, MaxLength(4)]
    public string Modifier { get; set; } = "";   // "" | "26" | "TC" | etc.

    [Required, MaxLength(8)]
    public string Locality { get; set; } = "";

    public int Year { get; set; }

    [Column(TypeName = "decimal(12,4)")] public decimal? NonFacilityRate { get; set; }
    [Column(TypeName = "decimal(12,4)")] public decimal? FacilityRate { get; set; }

    [Column(TypeName = "decimal(10,4)")] public decimal? WorkRvu { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? PeRvu { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? MpRvu { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? TotalRvu { get; set; }
    [Column(TypeName = "decimal(10,4)")] public decimal? ConversionFactor { get; set; }

    /// <summary>Full DKAN response payload, JSON-serialized — kept for audit.</summary>
    public string? RawPayloadJson { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    public DateTime CachedUntil { get; set; } = DateTime.UtcNow.AddHours(24);
}
