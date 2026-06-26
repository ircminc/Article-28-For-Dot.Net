using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW report 371.05 — Financial Analysis at CPT Level - Detail.
/// 72 columns, first 4 ECW spacers, one row per CPT line item.
public class EcwParser371 : EcwParserBase
{
    public static List<EcwCptLine> Parse(Stream stream, int batchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow = rows[0];
        var startCol  = FindDataStartCol(headerRow);
        var map       = BuildColMap(headerRow, startCol);
        var result    = new List<EcwCptLine>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            if (IsSummaryRow(row, startCol)) continue;
            var claimNo = Str(row, map, "Claim No");
            if (string.IsNullOrEmpty(claimNo)) continue;

            result.Add(new EcwCptLine
            {
                BatchId              = batchId,
                ClaimNo              = claimNo,
                PatientAcctNo        = Str(row, map, "Patient Acct No"),
                Patient              = Str(row, map, "Patient"),
                ServiceDate          = Date(row, map, "Service Date"),
                ClaimDate            = Date(row, map, "Claim Date"),
                PrimaryPayer         = Str(row, map, "Primary Payer"),
                Facility             = Str(row, map, "Facility"),
                FacilityPos          = Str(row, map, "Facility POS"),
                RenderingProvider    = Str(row, map, "Rendering Provider"),
                CptCode              = Str(row, map, "CPT Code"),
                CptDescription       = Str(row, map, "CPT Description"),
                CptGroupName         = Str(row, map, "CPT Group Name"),
                Modifier1            = Str(row, map, "Modifier 1"),
                Modifier2            = Str(row, map, "Modifier 2"),
                Modifier3            = Str(row, map, "Modifier 3"),
                Modifier4            = Str(row, map, "Modifier 4"),
                Icd1Code             = Str(row, map, "ICD1 Code"),
                Icd1Name             = Str(row, map, "ICD1 Name"),
                Icd2Code             = Str(row, map, "ICD2 Code"),
                Icd3Code             = Str(row, map, "ICD3 Code"),
                Icd4Code             = Str(row, map, "ICD4 Code"),
                BilledCharge         = Dec(row, map, "Billed Charge"),
                TotalPayment         = Dec(row, map, "Total Payment"),
                PayerPayment         = Dec(row, map, "Payer Payment"),
                PatientPayment       = Dec(row, map, "Patient Payment"),
                ContractualAdjustment= Dec(row, map, "Contractual Adjustment"),
                WriteoffAdjustment   = Dec(row, map, "Writeoff Adjustment"),
                Balance              = Dec(row, map, "Balance"),
                FeeScheduleAllowedFee= Dec(row, map, "Fee Schedule Allowed Fee"),
                BilledUnits          = Int(row, map, "Billed Units"),
                IsTelevisit          = Bool(row, map, "Is Televisit"),
            });
        }
        return result;
    }
}
