using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using APGAnalyzer.Services;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// Active provider configuration. Drives base-rate selection,
/// capital add-on eligibility, region resolution.
/// At most one row should have IsActive=true at a time. Old configs
/// are preserved (IsActive flipped to false) for audit history.
/// </summary>
[Table("provider_config")]
public class ProviderConfig : IOwnedByUser
{
    [Key]
    public int Id { get; set; }

    public bool IsActive { get; set; } = true;

    [Required, MaxLength(128)]
    public string ProviderName { get; set; } = "";

    [MaxLength(16)]
    public string? Npi { get; set; }

    public int? CountyCode { get; set; }

    /// <summary>'Upstate' | 'Downstate' — auto-derived from CountyCode at save.</summary>
    [MaxLength(16)]
    public string? Region { get; set; }

    /// <summary>e.g. 'Clinic*', 'Amb Surg', 'Renal', 'Clinic MR/DD/TBI'.</summary>
    [Required, MaxLength(64)]
    public string PeerGroup { get; set; } = "";

    /// <summary>'dtc' (freestanding) or 'hospital'.</summary>
    [Required, MaxLength(16)]
    public string ProviderType { get; set; } = "dtc";

    public bool CapitalAddonEligible { get; set; }

    [Column(TypeName = "decimal(12,4)")]
    public decimal? CapitalAddonRate { get; set; }

    [MaxLength(16)]
    public string? RateCodeOverride { get; set; }

    [MaxLength(16)]
    public string? CmsLocality { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// AspNetUsers.Id of the user who owns this provider config.
    /// Each user has their own active provider; isolation is enforced at
    /// the query layer via OwnedQueryExtensions. NULL on legacy rows
    /// (pre-isolation migration); the migration backfills these to the
    /// first admin so they remain visible to admins after upgrade.
    /// </summary>
    [MaxLength(450)]
    public string? OwnerUserId { get; set; }
}
