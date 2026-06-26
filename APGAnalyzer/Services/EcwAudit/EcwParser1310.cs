using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;

namespace APGAnalyzer.Services.EcwAudit;

/// Parses eCW report 13.10 — Progress Note Completion Date vs Claim Created Date.
public class EcwParser1310 : EcwParserBase
{
    public static List<EcwBillingLag> Parse(Stream stream, int batchId)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);
        var rows = ws.RowsUsed().ToList();
        if (rows.Count < 2) return new();

        var headerRow = rows[0];
        var startCol  = FindDataStartCol(headerRow);
        var map       = BuildColMap(headerRow, startCol);
        var result    = new List<EcwBillingLag>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            if (IsSummaryRow(row, startCol)) continue;
            var encId = Str(row, map, "Encounter ID");
            if (string.IsNullOrEmpty(encId)) continue;

            result.Add(new EcwBillingLag
            {
                BatchId                = batchId,
                EncounterId            = encId,
                PatientAcctNo          = Str(row, map, "Patient Acct No"),
                PatientName            = Str(row, map, "Patient Name"),
                Provider               = Str(row, map, "Appointment / Servicing Provider Name"),
                VisitType              = Str(row, map, "Visit Type"),
                AppointmentDate        = Date(row, map, "Appointment Date"),
                ChartLockStatus        = Str(row, map, "Chart Lock Status"),
                ProgressNoteLastLockedOn = Date(row, map, "Progress Note Last Locked On"),
                DaysApptToLocked       = NullableInt(row, map, "Days between Appt Date and Locked Date"),
                ClaimNo                = Str(row, map, "Claim No"),
                ClaimDate              = Date(row, map, "Claim Date"),
                DaysPnToClaimCreated   = NullableInt(row, map, "Days Between PN Locked On and Claim Created Date"),
                WorkflowStatus         = Str(row, map, "Status"),
            });
        }
        return result;
    }

    private static int? NullableInt(IXLRow row, Dictionary<string, int> map, string col)
    {
        if (!map.TryGetValue(col, out var c)) return null;
        var cell = row.Cell(c);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<double>(out var d)) return (int)d;
        if (int.TryParse(cell.GetString().Trim(), out var i)) return i;
        return null;
    }
}
