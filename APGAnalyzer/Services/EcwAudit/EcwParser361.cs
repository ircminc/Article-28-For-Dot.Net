using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW report 361.05 — Financial Analysis at Claim Level - Detail.
/// Expected: 51 columns, first 4 are ECW spacers, real header on row 1.
public class EcwParser361 : EcwParserBase
{
    public static List<EcwClaimFinancial> Parse(Stream stream, int batchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow  = rows[0];
        var startCol   = FindDataStartCol(headerRow);
        var map        = BuildColMap(headerRow, startCol);
        var result     = new List<EcwClaimFinancial>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            if (IsSummaryRow(row, startCol)) continue;
            var claimNo = Str(row, map, "Claim No");
            if (string.IsNullOrEmpty(claimNo)) continue;

            result.Add(new EcwClaimFinancial
            {
                BatchId              = batchId,
                ClaimNo              = claimNo,
                ServiceDate          = Date(row, map, "Service Date"),
                ClaimDate            = Date(row, map, "Claim Date"),
                ClaimStatusCode      = Str(row, map, "Claim Status Code"),
                ClaimStatusGroupName = Str(row, map, "Claim Status Group Name"),
                VisitType            = Str(row, map, "Visit Type"),
                PrimaryPayer         = Str(row, map, "Primary Payer"),
                SecondaryPayer       = Str(row, map, "Secondary Payer"),
                TertiaryPayer        = Str(row, map, "Tertiary Payer"),
                Facility             = Str(row, map, "Facility"),
                FacilityPos          = Str(row, map, "Facility POS"),
                AppointmentProvider  = Str(row, map, "Appointment / Servicing Provider"),
                RenderingProvider    = Str(row, map, "Rendering Provider"),
                Patient              = Str(row, map, "Patient"),
                PatientAcctNo        = Str(row, map, "Patient Acct No"),
                PatientAge           = Int(row, map, "Patient Age") is int a && a > 0 ? a : null,
                PatientGender        = Str(row, map, "Patient Gender"),
                ClaimVoided          = Str(row, map, "Claim Voided").Equals("Yes", StringComparison.OrdinalIgnoreCase),
                BilledCharge         = Dec(row, map, "Billed Charge"),
                PayerCharge          = Dec(row, map, "Payer Charge"),
                SelfCharge           = Dec(row, map, "Self Charge"),
                Payments             = Dec(row, map, "Payments"),
                PayerPayment         = Dec(row, map, "Payer Payment"),
                PatientPayment       = Dec(row, map, "Patient Payment"),
                ContractualAdjustment= Dec(row, map, "Contractual Adjustment"),
                PayerWithheld        = Dec(row, map, "Payer Withheld"),
                WriteoffAdjustment   = Dec(row, map, "Writeoff Adjustment"),
                Refund               = Dec(row, map, "Refund"),
                Balance              = Dec(row, map, "Balance"),
            });
        }
        return result;
    }
}
