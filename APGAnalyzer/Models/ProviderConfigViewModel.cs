using System.ComponentModel.DataAnnotations;
using APGAnalyzer.Services.Cms;

namespace APGAnalyzer.Models;

/// <summary>
/// Form-binding view model for the active-provider configuration. The
/// engine needs this to resolve region + select the correct base rate.
/// </summary>
public class ProviderConfigViewModel
{
    [Required, Display(Name = "Provider name")]
    public string ProviderName { get; set; } = "";

    [Display(Name = "NPI")]
    [RegularExpression(@"^\d{0,16}$", ErrorMessage = "NPI must be digits.")]
    public string? Npi { get; set; }

    [Display(Name = "County")]
    public int? CountyCode { get; set; }

    [Required, Display(Name = "Peer group")]
    public string PeerGroup { get; set; } = "";

    [Required, Display(Name = "Provider type")]
    public string ProviderType { get; set; } = "dtc";

    [Display(Name = "Capital add-on eligible")]
    public bool CapitalAddonEligible { get; set; }

    [Display(Name = "Capital add-on rate ($)")]
    public decimal? CapitalAddonRate { get; set; }

    [Display(Name = "Currently saved region")]
    public string? CurrentRegion { get; set; }

    [Display(Name = "CMS Medicare locality"), MaxLength(16)]
    public string? CmsLocality { get; set; }

    /// <summary>Populated from provider_county for the dropdown.</summary>
    public List<(int Code, string Name, string Region)> AllCounties { get; set; } = new();

    /// <summary>Populated from apg_base_rates for the dropdown.</summary>
    public List<string> AllPeerGroups { get; set; } = new();

    /// <summary>Populated by a live call to the CMS catalog (cached 24h).</summary>
    public IReadOnlyList<CmsLocality> AllCmsLocalities { get; set; } = Array.Empty<CmsLocality>();
}
