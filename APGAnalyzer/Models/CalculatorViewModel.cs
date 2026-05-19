using System.ComponentModel.DataAnnotations;
using APGAnalyzer.Models.Engine;

namespace APGAnalyzer.Models;

/// <summary>
/// Form-binding view model for the Rate Calculator. Inputs on top,
/// computed result properties on the bottom (only set after Calculate
/// runs, otherwise null/empty).
/// </summary>
public class CalculatorViewModel
{
    // -- Inputs --
    [Required, Display(Name = "Date of service")]
    [DataType(DataType.Date)]
    public DateOnly DateOfService { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Display(Name = "Principal ICD-10")]
    [MaxLength(16)]
    public string? PrincipalDiagnosis { get; set; }

    /// <summary>Which rate source(s) to compute.</summary>
    [Display(Name = "Rate source")]
    public RateSource Source { get; set; } = RateSource.Apg;

    /// <summary>For CMS lookups: the locality. Defaults to provider config's CmsLocality.</summary>
    [Display(Name = "CMS locality"), MaxLength(16)]
    public string? CmsLocality { get; set; }

    /// <summary>If true, CMS lookup uses facility PE RVU (hospital outpatient
    /// setting); otherwise non-facility (independent clinic / office).</summary>
    [Display(Name = "Use facility rate (CMS)")]
    public bool CmsUseFacilityRate { get; set; }

    /// <summary>If true, CMS lookup also fetches the -26 (professional) and
    /// -TC (technical) modifier rows for each procedure, where they exist.</summary>
    [Display(Name = "Include PC/TC split (CMS)")]
    public bool CmsIncludePcTc { get; set; } = true;

    /// <summary>Service-line procedure codes the user typed. Multi-line is
    /// dynamic via JS — minimum 1 row, no hard maximum.</summary>
    public List<CalculatorLineInput> ServiceLines { get; set; } = new()
    {
        new CalculatorLineInput { Units = 1 },
    };

    // -- Outputs (populated by Calculate) --
    public APGResult? Result { get; set; }
    public ICDBasedEAPG? IcdBasedResult { get; set; }
    public CmsCalculatorResult? CmsResult { get; set; }
    public string? Warning { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ProviderConfigured { get; set; }

    public bool ShouldComputeApg => Source == RateSource.Apg || Source == RateSource.Both;
    public bool ShouldComputeCms => Source == RateSource.Cms || Source == RateSource.Both;
}

public enum RateSource
{
    /// <summary>NYS DOH Article 28 APG payment (institutional / outpatient).</summary>
    Apg,
    /// <summary>CMS Medicare Physician Fee Schedule (professional).</summary>
    Cms,
    /// <summary>Compute both side-by-side.</summary>
    Both,
}

public class CalculatorLineInput
{
    [Display(Name = "CPT/HCPCS")]
    public string? ProcedureCode { get; set; }

    [Display(Name = "Modifiers (comma-separated)")]
    public string? Modifiers { get; set; }

    [Display(Name = "Units"), Range(1, 999)]
    public int Units { get; set; } = 1;

    [Display(Name = "Paid $")]
    public decimal? BilledAmount { get; set; }
}

/// <summary>CMS calculation block — sits alongside the APG result in the calculator UI.</summary>
public class CmsCalculatorResult
{
    public string Locality { get; set; } = "";
    public int Year { get; set; }
    public bool UsedFacilityRate { get; set; }
    public List<CmsCalculatorLine> Lines { get; set; } = new();

    /// <summary>Banner-level message (e.g. CMS catalog unreachable).</summary>
    public string? Banner { get; set; }

    public decimal TotalExpected => Lines.Sum(l => l.ExpectedPayment ?? 0m);
    public decimal TotalPaid     => Lines.Sum(l => l.PaidAmount);
    public decimal Variance      => TotalExpected - TotalPaid;
}

public class CmsCalculatorLine
{
    public int LineSeq { get; set; }
    public string ProcedureCode { get; set; } = "";
    public string Modifier { get; set; } = "";
    public int Units { get; set; }
    public decimal PaidAmount { get; set; }

    public decimal? NonFacilityRate { get; set; }
    public decimal? FacilityRate { get; set; }
    public decimal? ProfessionalRate { get; set; }   // -26 modifier row
    public decimal? TechnicalRate { get; set; }      // -TC modifier row

    public decimal? WorkRvu { get; set; }
    public decimal? PeRvu { get; set; }
    public decimal? MpRvu { get; set; }
    public decimal? TotalRvu { get; set; }
    public decimal? ConversionFactor { get; set; }

    public decimal? ExpectedPayment { get; set; }    // chosen rate × units
    public decimal Variance => (ExpectedPayment ?? 0m) - PaidAmount;
    public string? Error { get; set; }
}
