using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW report 31.08 — Patient Balance Aging Report - Detail.
/// 57 columns with complex header; ignores patient subtotal rows.
public class EcwParser3108 : EcwParserBase
{
    public static List<EcwPatientAging> Parse(Stream stream, int batchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow = rows[0];
        // 31.08 header starts at col 1 (Patient Name), no leading spacers
        var map = BuildColMap(headerRow, 1);
        var result = new List<EcwPatientAging>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            var claimNo = Str(row, map, "Claim No");
            if (string.IsNullOrEmpty(claimNo)) continue;
            // Skip subtotal rows (no claim number)
            if (!decimal.TryParse(claimNo, out _) && claimNo.Length < 3) continue;

            result.Add(new EcwPatientAging
            {
                BatchId           = batchId,
                PatientName       = Str(row, map, "Patient Name.1"),
                PatientAcctNo     = Str(row, map, "Patient Acct No"),
                PatientDob        = Date(row, map, "Patient DOB"),
                ClaimNo           = claimNo,
                ClaimDate         = Date(row, map, "Claim Date"),
                ServiceDate       = Date(row, map, "Service Date"),
                ClaimAmount       = Dec(row, map, "Claim Amount"),
                Balance           = Dec(row, map, "Balance"),
                Days0To30         = Dec(row, map, "0 - 30 Days"),
                Days31To60        = Dec(row, map, "31 - 60 Days"),
                Days61To90        = Dec(row, map, "61 - 90 Days"),
                Days91To120       = Dec(row, map, "91 - 120 Days"),
                Days121To150      = Dec(row, map, "121 - 150 Days"),
                Days151To180      = Dec(row, map, "151 - 180 Days"),
                DaysOver180       = Dec(row, map, "> 180 Days"),
                NoOfStatementsSent= Int(row, map, "No. of Statements Sent"),
            });
        }
        return result;
    }
}
