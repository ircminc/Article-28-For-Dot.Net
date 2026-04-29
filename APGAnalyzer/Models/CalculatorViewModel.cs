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

    /// <summary>Up to 12 service-line procedure codes the user typed.</summary>
    public List<CalculatorLineInput> ServiceLines { get; set; } = new()
    {
        new CalculatorLineInput { Units = 1 },
    };

    // -- Outputs (populated by Calculate) --
    public APGResult? Result { get; set; }
    public ICDBasedEAPG? IcdBasedResult { get; set; }
    public string? Warning { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ProviderConfigured { get; set; }
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
