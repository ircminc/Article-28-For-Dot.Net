using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW reports 31.09 Primary and Secondary — Payer Claim Aging.
/// Both files share the same 29-column structure.
public class EcwParser3109 : EcwParserBase
{
    public static List<EcwPayerAging> Parse(Stream stream, int batchId, bool isPrimary)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow = rows[0];
        // First column is an ECW spacer (None); real data starts at Payer Name
        var startCol  = FindDataStartCol(headerRow);
        var map       = BuildColMap(headerRow, startCol);
        var result    = new List<EcwPayerAging>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            if (IsSummaryRow(row, startCol)) continue;
            var claimNo = Str(row, map, "Claim No");
            if (string.IsNullOrEmpty(claimNo)) continue;

            result.Add(new EcwPayerAging
            {
                BatchId                = batchId,
                IsPrimary              = isPrimary,
                PayerName              = Str(row, map, "Payer Name"),
                PatientName            = Str(row, map, "Patient Name"),
                PatientAcctNo          = Str(row, map, "Patient Acct No"),
                AgingDays              = Int(row, map, "Aging Days"),
                ClaimDate              = Date(row, map, "Claim Date"),
                ServiceDate            = Date(row, map, "Service Date"),
                ClaimFirstSubmittedDate= Date(row, map, "Claim First Submitted Date"),
                LastSubmissionDate     = Date(row, map, "Last Submission Date"),
                ClaimNo                = claimNo,
                Charges                = Dec(row, map, "Charges"),
                DaysCurrent            = Dec(row, map, "Current"),
                Days31To60             = Dec(row, map, "31-60"),
                Days61To90             = Dec(row, map, "61-90"),
                Days91To120            = Dec(row, map, "91-120"),
                DaysOver120            = Dec(row, map, "> 120"),
                Balance                = Dec(row, map, "Balance"),
            });
        }
        return result;
    }
}
