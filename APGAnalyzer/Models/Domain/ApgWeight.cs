using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace APGAnalyzer.Models.Domain;

/// <summary>
/// EAPG relative weight by effective date, long-form. One row per
/// (apg, effective_date). At query time the engine selects the most-recent
/// effective_date ≤ DOS.
///
/// "Final rate" rows use the sentinel effective_date 9999-12-31 with
/// <see cref="IsFinalRate"/> = true and <see cref="YearRate"/> set to the
/// year they cover. The engine prefers them when YearRate ≥ year(DOS).
/// </summary>
[Table("apg_weights")]
public class ApgWeight
{
    [Key]
    public int Id { get; set; }

    public int Apg { get; set; }

    [MaxLength(512)]
    public string? ApgDescription { get; set; }

    public DateOnly EffectiveDate { get; set; }

    [Column(TypeName = "decimal(12,6)")]
    public decimal Weight { get; set; }

    public bool IsFinalRate { get; set; }

    public int? YearRate { get; set; }
}
