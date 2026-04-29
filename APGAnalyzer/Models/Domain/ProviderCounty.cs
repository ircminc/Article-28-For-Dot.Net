using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// NY county → Upstate/Downstate region mapping. Drives region resolution
/// when a provider config has county_code but no explicit region.
///
/// Note: NYC boroughs ARE in this table, but Manhattan is labeled
/// 'MANHATTAN' (county_code 60), not 'NEW YORK'.
/// </summary>
[Table("provider_county")]
public class ProviderCounty
{
    [Key]
    public int CountyCode { get; set; }

    [Required, MaxLength(64)]
    public string CountyName { get; set; } = "";

    [MaxLength(32)]
    public string? HealthHomePhase { get; set; }

    /// <summary>'Upstate' or 'Downstate'.</summary>
    [Required, MaxLength(16)]
    public string Region { get; set; } = "";
}
