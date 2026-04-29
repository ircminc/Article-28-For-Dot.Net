using System.Text.Json;
using APGAnalyzer.Models;
using APGAnalyzer.Models.Domain;
using APGAnalyzer.Models.Engine;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace APGAnalyzer.Services;

/// <summary>
/// Generates Excel + PDF exports. Pure conversion logic — controllers
/// fetch the data, this service formats it.
/// </summary>
public class ExportService
{
    /// <summary>
    /// Excel rendering of a Claims-list query result. Used for both the
    /// filtered-list export and any future bulk reports.
    /// </summary>
    public byte[] BuildClaimsListXlsx(IReadOnlyList<ClaimsListRow> rows, string sheetTitle = "Claims")
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(sheetTitle);

        // Header
        var headers = new[]
        {
            "Type", "Claim ID", "DOS", "Patient", "Payer",
            "Billed", "Paid", "Correct APG", "Variance", "Status",
        };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        var headerRange = ws.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        // Rows
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowIdx = i + 2;
            ws.Cell(rowIdx, 1).Value = r.FileType;
            ws.Cell(rowIdx, 2).Value = r.ClaimId;
            ws.Cell(rowIdx, 3).Value = r.DateOfService?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(rowIdx, 4).Value = r.PatientName ?? "";
            ws.Cell(rowIdx, 5).Value = r.PayerName ?? "";
            ws.Cell(rowIdx, 6).Value = r.BilledAmount;
            ws.Cell(rowIdx, 7).Value = r.PaidAmount;
            ws.Cell(rowIdx, 8).Value = r.CorrectApgPayment;
            ws.Cell(rowIdx, 9).Value = r.Variance;
            ws.Cell(rowIdx, 10).Value =
                r.Underpaid == true ? "Underpaid"
              : r.Overpaid == true ? "Overpaid"
              : (r.CorrectApgPayment.HasValue ? "Match" : "Unpriced");
        }

        // Format money columns
        var moneyCols = ws.Range(2, 6, Math.Max(2, rows.Count + 1), 9);
        moneyCols.Style.NumberFormat.Format = "$#,##0.00;[Red]-$#,##0.00";

        ws.Columns().AdjustToContents();
        // Cap any over-wide column to a reasonable width
        foreach (var col in ws.Columns())
            if (col.Width > 40) col.Width = 40;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Excel rendering of a single claim — header info on top, parsed
    /// service lines + APG result + CAS adjustments on separate sheets.
    /// </summary>
    public byte[] BuildClaimDetailXlsx(ParsedClaim claim, ApgResultRecord? apg, List<APGLineResult> apgLines)
    {
        using var wb = new XLWorkbook();

        // --- Sheet 1: Header ---
        var hdr = wb.AddWorksheet("Claim");
        AddKeyValue(hdr, "Claim ID", claim.ClaimId, 1);
        AddKeyValue(hdr, "File type", claim.FileType, 2);
        AddKeyValue(hdr, "Date of service", claim.DateOfService?.ToString("yyyy-MM-dd") ?? "—", 3);
        AddKeyValue(hdr, "Status", claim.ClaimStatus ?? "—", 4);
        AddKeyValue(hdr, "Patient", claim.PatientName ?? "—", 5);
        AddKeyValue(hdr, "Patient ID", claim.PatientId ?? "—", 6);
        AddKeyValue(hdr, "Provider", claim.ProviderName ?? "—", 7);
        AddKeyValue(hdr, "Provider NPI", claim.ProviderNpi ?? "—", 8);
        AddKeyValue(hdr, "Payer", claim.PayerName ?? "—", 9);
        AddKeyValue(hdr, "Principal dx", claim.PrincipalDiagnosis ?? "—", 10);
        AddKeyValue(hdr, "Billed", claim.BilledAmount.ToString("C2"), 11);
        AddKeyValue(hdr, "Allowed", claim.AllowedAmount.ToString("C2"), 12);
        AddKeyValue(hdr, "Paid", claim.PaidAmount.ToString("C2"), 13);
        AddKeyValue(hdr, "Patient resp.", claim.PatientResponsibility.ToString("C2"), 14);
        if (apg is not null)
        {
            AddKeyValue(hdr, "Peer group", apg.PeerGroup, 16);
            AddKeyValue(hdr, "Region", apg.Region, 17);
            AddKeyValue(hdr, "Base rate", apg.BaseRateApplied.ToString("C2"), 18);
            AddKeyValue(hdr, "Correct APG", apg.CorrectApgPayment.ToString("C2"), 19);
            AddKeyValue(hdr, "Variance", apg.Variance.ToString("C2"), 20);
            AddKeyValue(hdr, "Status",
                apg.Underpaid ? "Underpaid"
              : apg.Overpaid ? "Overpaid"
              : "Match", 21);
        }
        hdr.Columns().AdjustToContents();

        // --- Sheet 2: Parsed lines (from EDI) ---
        var lines = wb.AddWorksheet("EDI Lines");
        var lineHdr = new[] { "#", "Procedure", "Modifiers", "Rev code", "Units", "Billed", "Allowed", "Paid" };
        for (int c = 0; c < lineHdr.Length; c++) lines.Cell(1, c + 1).Value = lineHdr[c];
        lines.Range(1, 1, 1, lineHdr.Length).Style.Font.Bold = true;
        lines.Range(1, 1, 1, lineHdr.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");

        int row = 2;
        foreach (var sl in claim.ServiceLines.OrderBy(x => x.LineSeq))
        {
            var mods = string.IsNullOrEmpty(sl.ModifiersJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(sl.ModifiersJson) ?? new();
            lines.Cell(row, 1).Value = sl.LineSeq;
            lines.Cell(row, 2).Value = sl.ProcedureCode;
            lines.Cell(row, 3).Value = string.Join(", ", mods);
            lines.Cell(row, 4).Value = sl.RevenueCode ?? "";
            lines.Cell(row, 5).Value = sl.Units;
            lines.Cell(row, 6).Value = sl.BilledAmount;
            lines.Cell(row, 7).Value = sl.AllowedAmount;
            lines.Cell(row, 8).Value = sl.PaidAmount;
            row++;
        }
        lines.Range(2, 6, Math.Max(2, row - 1), 8).Style.NumberFormat.Format = "$#,##0.00";
        lines.Columns().AdjustToContents();

        // --- Sheet 3: APG per-line math ---
        if (apgLines.Count > 0)
        {
            var apgWs = wb.AddWorksheet("APG Math");
            var apgHdr = new[]
            {
                "#", "Procedure", "EAPG", "EAPG type", "Weight",
                "Base rate", "Expected", "Paid", "Variance",
                "Packaged", "Discounted", "Notes",
            };
            for (int c = 0; c < apgHdr.Length; c++) apgWs.Cell(1, c + 1).Value = apgHdr[c];
            apgWs.Range(1, 1, 1, apgHdr.Length).Style.Font.Bold = true;
            apgWs.Range(1, 1, 1, apgHdr.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");

            int aRow = 2;
            foreach (var ld in apgLines)
            {
                apgWs.Cell(aRow, 1).Value = ld.LineSeq;
                apgWs.Cell(aRow, 2).Value = ld.ProcedureCode;
                apgWs.Cell(aRow, 3).Value = ld.Eapg;
                apgWs.Cell(aRow, 4).Value = ld.EapgTypeRaw ?? ld.EapgType.ToString();
                apgWs.Cell(aRow, 5).Value = ld.Weight ?? 0;
                apgWs.Cell(aRow, 6).Value = ld.BaseRate;
                apgWs.Cell(aRow, 7).Value = ld.ExpectedPayment;
                apgWs.Cell(aRow, 8).Value = ld.ActualPaid;
                apgWs.Cell(aRow, 9).Value = ld.Variance;
                apgWs.Cell(aRow, 10).Value = ld.Packaged ? "Yes" : "No";
                apgWs.Cell(aRow, 11).Value = ld.Discounted ? "Yes" : "No";
                apgWs.Cell(aRow, 12).Value = string.Join(" | ", ld.Notes);
                aRow++;
            }
            apgWs.Range(2, 5, Math.Max(2, aRow - 1), 9).Style.NumberFormat.Format = "$#,##0.00";
            apgWs.Cell(1, 5).WorksheetColumn().Width = 12;
            apgWs.Columns().AdjustToContents();
            foreach (var col in apgWs.Columns())
                if (col.Width > 60) col.Width = 60;
        }

        // --- Sheet 4: CAS adjustments ---
        if (claim.Adjustments.Count > 0)
        {
            var cas = wb.AddWorksheet("CAS Adjustments");
            var casHdr = new[] { "Scope", "Group", "Reason", "Amount", "Quantity" };
            for (int c = 0; c < casHdr.Length; c++) cas.Cell(1, c + 1).Value = casHdr[c];
            cas.Range(1, 1, 1, casHdr.Length).Style.Font.Bold = true;
            cas.Range(1, 1, 1, casHdr.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");

            int cRow = 2;
            foreach (var adj in claim.Adjustments)
            {
                cas.Cell(cRow, 1).Value = adj.LineSeq.HasValue ? $"Line {adj.LineSeq}" : "Claim";
                cas.Cell(cRow, 2).Value = adj.GroupCode;
                cas.Cell(cRow, 3).Value = adj.ReasonCode;
                cas.Cell(cRow, 4).Value = adj.Amount;
                cas.Cell(cRow, 5).Value = adj.Quantity ?? 0;
                cRow++;
            }
            cas.Range(2, 4, Math.Max(2, cRow - 1), 4).Style.NumberFormat.Format = "$#,##0.00";
            cas.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>PDF rendering of a single claim — the printable summary.</summary>
    public byte[] BuildClaimDetailPdf(ParsedClaim claim, ApgResultRecord? apg, List<APGLineResult> apgLines)
    {
        // QuestPDF requires an explicit license setting at process start.
        // Set Community here defensively in case Program.cs hasn't.
        QuestPDF.Settings.License = LicenseType.Community;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));

                // ---- Header ----
                page.Header().Column(col =>
                {
                    col.Item().Text(text =>
                    {
                        text.Span("APG Rate Analyzer — Claim ").FontSize(14).SemiBold();
                        text.Span(claim.ClaimId).FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                    });
                    col.Item().Text(text =>
                    {
                        text.Span($"{claim.FileType}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        text.Span($"   ·   uploaded {claim.CreatedAt:yyyy-MM-dd HH:mm}").FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                    col.Item().PaddingTop(5).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten2);
                });

                // ---- Body ----
                page.Content().PaddingVertical(12).Column(body =>
                {
                    body.Spacing(10);

                    // Two-column header info
                    body.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            KvLine(c, "Date of service", claim.DateOfService?.ToString("yyyy-MM-dd") ?? "—");
                            KvLine(c, "Status", claim.ClaimStatus ?? "—");
                            KvLine(c, "Patient", claim.PatientName ?? "—");
                            KvLine(c, "Patient ID", claim.PatientId ?? "—");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            KvLine(c, "Provider", claim.ProviderName ?? "—");
                            KvLine(c, "Provider NPI", claim.ProviderNpi ?? "—");
                            KvLine(c, "Payer", claim.PayerName ?? "—");
                            KvLine(c, "Principal dx", claim.PrincipalDiagnosis ?? "—");
                        });
                    });

                    // Money summary band
                    body.Item().Background(Colors.Grey.Lighten4).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Column(c => MoneyTile(c, "Billed", claim.BilledAmount));
                        row.RelativeItem().Column(c => MoneyTile(c, "Allowed", claim.AllowedAmount));
                        row.RelativeItem().Column(c => MoneyTile(c, "Paid", claim.PaidAmount));
                        row.RelativeItem().Column(c => MoneyTile(c, "Patient resp.", claim.PatientResponsibility));
                    });

                    // APG result
                    if (apg is not null)
                    {
                        body.Item().Text("APG Result — Article 28").FontSize(12).SemiBold();
                        body.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                KvLine(c, "Peer group", apg.PeerGroup);
                                KvLine(c, "Region", apg.Region);
                                KvLine(c, "Base rate", apg.BaseRateApplied.ToString("C2"));
                            });
                            row.RelativeItem().Column(c =>
                            {
                                KvLine(c, "Correct APG", apg.CorrectApgPayment.ToString("C2"));
                                KvLine(c, "Variance", apg.Variance.ToString("C2"));
                                KvLine(c, "Status",
                                    apg.Underpaid ? "Underpaid"
                                  : apg.Overpaid ? "Overpaid"
                                  : "Match");
                            });
                        });

                        // Per-line APG table
                        if (apgLines.Count > 0)
                        {
                            body.Item().Text("Per-line math").FontSize(11).SemiBold().FontColor(Colors.Grey.Darken2);
                            body.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(20);
                                    c.RelativeColumn(1.2f);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(0.9f);
                                    c.RelativeColumn(0.9f);
                                });
                                t.Header(h =>
                                {
                                    foreach (var lbl in new[] { "#", "Procedure · EAPG", "Calculation", "Expected", "Paid" })
                                    {
                                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                            .Text(lbl).FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                                    }
                                });
                                foreach (var ld in apgLines)
                                {
                                    t.Cell().Text(ld.LineSeq.ToString());
                                    t.Cell().Column(col =>
                                    {
                                        col.Item().Text(ld.ProcedureCode).SemiBold();
                                        if (ld.Eapg.HasValue)
                                            col.Item().Text($"EAPG {ld.Eapg} · {ld.EapgTypeRaw ?? ld.EapgType.ToString()}")
                                                       .FontSize(8).FontColor(Colors.Grey.Darken1);
                                    });
                                    t.Cell().Column(col =>
                                    {
                                        if (ld.Packaged)
                                            col.Item().Text("Packaged — no separate payment.").Italic().FontColor(Colors.Grey.Darken2);
                                        else if (ld.Weight is > 0)
                                            col.Item().Text($"{ld.Weight!.Value:0.0000} × {ld.BaseRate:C2} = {ld.ExpectedPayment:C2}");
                                        if (ld.Notes.Count > 0)
                                            col.Item().Text(string.Join("\n", ld.Notes))
                                                       .FontSize(8).FontColor(Colors.Grey.Darken1);
                                    });
                                    t.Cell().AlignRight().Text(ld.ExpectedPayment.ToString("C2"));
                                    t.Cell().AlignRight().Text(ld.ActualPaid.ToString("C2"));
                                }
                            });
                        }
                    }

                    // Adjustments
                    if (claim.Adjustments.Count > 0)
                    {
                        body.Item().Text("CAS adjustments").FontSize(11).SemiBold().FontColor(Colors.Grey.Darken2);
                        body.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(); c.RelativeColumn();
                                c.RelativeColumn(); c.RelativeColumn();
                            });
                            t.Header(h =>
                            {
                                foreach (var lbl in new[] { "Scope", "Group", "Reason", "Amount" })
                                {
                                    h.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                                        .Text(lbl).FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                                }
                            });
                            foreach (var adj in claim.Adjustments)
                            {
                                t.Cell().Text(adj.LineSeq.HasValue ? $"Line {adj.LineSeq}" : "Claim");
                                t.Cell().Text(adj.GroupCode);
                                t.Cell().Text(adj.ReasonCode);
                                t.Cell().AlignRight().Text(adj.Amount.ToString("C2"));
                            }
                        });
                    }
                });

                // ---- Footer ----
                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("APG Rate Analyzer · ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span($"generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    // -----------------------------------------------------------------
    // Tiny helpers (kept private so the public surface is just the
    // three Build... methods).
    // -----------------------------------------------------------------
    private static void AddKeyValue(IXLWorksheet ws, string key, string value, int row)
    {
        ws.Cell(row, 1).Value = key;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = value;
    }

    private static void KvLine(QuestPDF.Fluent.ColumnDescriptor col, string key, string value)
        => col.Item().Text(text =>
        {
            text.Span($"{key}: ").FontSize(9).FontColor(Colors.Grey.Darken1);
            text.Span(value).FontSize(10);
        });

    private static void MoneyTile(QuestPDF.Fluent.ColumnDescriptor col, string label, decimal v)
    {
        col.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor(Colors.Grey.Darken1);
        col.Item().Text(v.ToString("C2")).FontSize(13).SemiBold();
    }

}
