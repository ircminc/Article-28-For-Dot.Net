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
    /// <summary>
    /// Natural key — uses NYS DOH's published numeric county code
    /// (e.g. 60 = MANHATTAN, 58 = BRONX, 29 = NIAGARA). NOT auto-
    /// generated; <see cref="DatabaseGeneratedOption.None"/> tells EF
    /// Core to honor the explicit value at insert time. Without this,
    /// EF treats int PKs as IDENTITY, and SQL Server rejects explicit
    /// value inserts unless you flip SET IDENTITY_INSERT.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int CountyCode { get; set; }

    [Required, MaxLength(64)]
    public string CountyName { get; set; } = "";

    [MaxLength(32)]
    public string? HealthHomePhase { get; set; }

    /// <summary>'Upstate' or 'Downstate'.</summary>
    [Required, MaxLength(16)]
    public string Region { get; set; } = "";
}
