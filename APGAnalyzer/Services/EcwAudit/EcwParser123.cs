using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW report 123.06 — Claim Submission Report.
/// One row per submission event; multiple rows per claim on resubmissions.
public class EcwParser123 : EcwParserBase
{
    public static List<EcwSubmission> Parse(Stream stream, int batchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow = rows[0];
        var startCol  = FindDataStartCol(headerRow);
        var map       = BuildColMap(headerRow, startCol);
        var result    = new List<EcwSubmission>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            if (IsSummaryRow(row, startCol)) continue;
            var claimNo = Str(row, map, "Claim No");
            if (string.IsNullOrEmpty(claimNo)) continue;

            result.Add(new EcwSubmission
            {
                BatchId                  = batchId,
                ClaimNo                  = claimNo,
                PatientAcctNo            = Str(row, map, "Patient Acct No"),
                PatientName              = Str(row, map, "Patient Name"),
                ServiceDate              = Date(row, map, "Service Date"),
                ClaimDate                = Date(row, map, "Claim Date"),
                SubmissionType           = Str(row, map, "Submission Type"),
                SubmissionDate           = Date(row, map, "Submission Date"),
                ClaimFirstSubmissionDate = Date(row, map, "Claim First Submission Date"),
                ClaimLastSubmissionDate  = Date(row, map, "Claim Last Submission Date"),
                PayerName                = Str(row, map, "Payer Name"),
                SubmissionCount          = Int(row, map, "Submission Count"),
                Charges                  = Dec(row, map, "Charges"),
                LogMessage               = Str(row, map, "Log Message"),
            });
        }
        return result;
    }
}
