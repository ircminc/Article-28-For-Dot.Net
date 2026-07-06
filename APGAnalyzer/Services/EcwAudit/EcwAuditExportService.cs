using APGAnalyzer.Models.Domain;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace APGAnalyzer.Services.EcwAudit;

public interface IEcwAuditExportService
{
    byte[] ToExcel(EcwAuditBatch batch, List<AuditCheckResult> results);
    byte[] ToCsv(EcwAuditBatch batch, List<AuditCheckResult> results);
    byte[] ToPdf(EcwAuditBatch batch, List<AuditCheckResult> results);
}

public class EcwAuditExportService : IEcwAuditExportService
{
    // Excel
    public byte[] ToExcel(EcwAuditBatch batch, List<AuditCheckResult> results)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Summary");
        ws.Cell(1, 1).Value = "ECW Practice Audit Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Practice:";   ws.Cell(2, 2).Value = batch.PracticeName;
        ws.Cell(3, 1).Value = "Audit Date:"; ws.Cell(3, 2).Value = batch.AuditDate.ToString("MM/dd/yyyy");
        ws.Cell(4, 1).Value = "Run Date:";   ws.Cell(4, 2).Value = DateTime.Now.ToString("MM/dd/yyyy h:mm tt");

        int row = 6;
        var headers = new[] { "#", "Check", "Score", "Benchmark", "Status" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(row, c + 1).Value = headers[c];
            ws.Cell(row, c + 1).Style.Font.Bold = true;
            ws.Cell(row, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#343a40");
            ws.Cell(row, c + 1).Style.Font.FontColor = XLColor.White;
        }
        row++;
        foreach (var r in results)
        {
            ws.Cell(row, 1).Value = r.CheckId;
            ws.Cell(row, 2).Value = r.CheckName;
            ws.Cell(row, 3).Value = r.Score;
            ws.Cell(row, 4).Value = r.Benchmark;
            ws.Cell(row, 5).Value = r.Status.ToString();
            var statusColor = r.Status switch
            {
                AuditStatus.Pass => XLColor.FromHtml("#d1e7dd"),
                AuditStatus.Warn => XLColor.FromHtml("#fff3cd"),
                AuditStatus.Fail => XLColor.FromHtml("#f8d7da"),
                _                => XLColor.FromHtml("#e2e3e5"),
            };
            for (int c = 1; c <= 5; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = statusColor;
            row++;
        }
        ws.Columns().AdjustToContents();

        foreach (var r in results)
        {
            var sheetName = $"C{r.CheckId}-{SanitizeSheetName(r.CheckName)}";
            var ds = wb.AddWorksheet(sheetName);
            ds.Cell(1, 1).Value = $"Check {r.CheckId}: {r.CheckName}";
            ds.Cell(1, 1).Style.Font.Bold = true;
            ds.Cell(2, 1).Value = r.Summary;
            ds.Cell(2, 1).Style.Font.Italic = true;
            ds.Cell(3, 1).Value = $"Score: {r.Score}   |   Status: {r.Status}   |   Benchmark: {r.Benchmark}";

            int dr = 5;
            if (r.DetailRows.Any())
            {
                ds.Cell(dr, 1).Value = "Detail"; ds.Cell(dr, 1).Style.Font.Bold = true; dr++;
                foreach (var d in r.DetailRows) { ds.Cell(dr, 1).Value = d.Label; ds.Cell(dr, 2).Value = d.Value; dr++; }
                dr++;
            }
            if (r.FlagRows.Any())
            {
                ds.Cell(dr, 1).Value = "Flagged Claims"; ds.Cell(dr, 1).Style.Font.Bold = true; dr++;
                var fh = new[] { "Claim #", "Patient", "Service Date", "Payer", "Detail", "Amount" };
                for (int c = 0; c < fh.Length; c++)
                {
                    ds.Cell(dr, c + 1).Value = fh[c];
                    ds.Cell(dr, c + 1).Style.Font.Bold = true;
                    ds.Cell(dr, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#6c757d");
                    ds.Cell(dr, c + 1).Style.Font.FontColor = XLColor.White;
                }
                dr++;
                foreach (var f in r.FlagRows)
                {
                    ds.Cell(dr, 1).Value = f.ClaimNo ?? ""; ds.Cell(dr, 2).Value = f.Patient ?? "";
                    ds.Cell(dr, 3).Value = f.ServiceDate ?? ""; ds.Cell(dr, 4).Value = f.Payer ?? "";
                    ds.Cell(dr, 5).Value = f.FlagDetail ?? "";
                    if (f.Amount.HasValue) ds.Cell(dr, 6).Value = f.Amount.Value;
                    dr++;
                }
            }
            ds.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string SanitizeSheetName(string name)
    {
        var s = new string(name.Where(c => c != ':' && c != '/' && c != '\\' && c != '?' && c != '*' && c != '[' && c != ']').ToArray());
        return s.Length > 25 ? s[..25] : s;
    }

    // CSV
    public byte[] ToCsv(EcwAuditBatch batch, List<AuditCheckResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ECW Practice Audit Report — {batch.PracticeName} — {batch.AuditDate:MM/dd/yyyy}");
        sb.AppendLine();
        sb.AppendLine("Check #,Check Name,Score,Benchmark,Status,Summary");
        foreach (var r in results)
            sb.AppendLine($"{r.CheckId},{Csv(r.CheckName)},{Csv(r.Score)},{Csv(r.Benchmark)},{r.Status},{Csv(r.Summary)}");
        sb.AppendLine();
        foreach (var r in results.Where(r => r.FlagRows.Any()))
        {
            sb.AppendLine($"Check {r.CheckId} — {r.CheckName} — Flagged Claims");
            sb.AppendLine("Claim #,Patient,Service Date,Payer,Detail,Amount");
            foreach (var f in r.FlagRows)
                sb.AppendLine($"{Csv(f.ClaimNo)},{Csv(f.Patient)},{Csv(f.ServiceDate)},{Csv(f.Payer)},{Csv(f.FlagDetail)},{f.Amount:F2}");
            sb.AppendLine();
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(string? v)
    {
        if (v is null) return "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }

    // PDF
    public byte[] ToPdf(EcwAuditBatch batch, List<AuditCheckResult> results)
    {
        int pass = results.Count(r => r.Status == AuditStatus.Pass);
        int warn = results.Count(r => r.Status == AuditStatus.Warn);
        int fail = results.Count(r => r.Status == AuditStatus.Fail);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily("Arial"));

                page.Header().BorderBottom(1).BorderColor("#dee2e6").PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("ECW Practice Audit Report").Bold().FontSize(14);
                        col.Item().Text($"{batch.PracticeName}  |  Audit Date: {batch.AuditDate:MM/dd/yyyy}  |  Run: {DateTime.Now:MM/dd/yyyy h:mm tt}")
                            .FontSize(8).FontColor("#6c757d");
                    });
                    row.ConstantItem(120).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Pass: {pass}").FontColor("#198754").Bold();
                        col.Item().Text($"Warn: {warn}").FontColor("#856404").Bold();
                        col.Item().Text($"Fail: {fail}").FontColor("#dc3545").Bold();
                    });
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().PaddingBottom(4).Text("Audit Summary").Bold().FontSize(11);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(20); c.RelativeColumn(3); c.RelativeColumn(2);
                            c.RelativeColumn(3); c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            foreach (var hdr in new[] { "#", "Check", "Score", "Benchmark", "Status" })
                                h.Cell().Background("#343a40").Padding(3).Text(hdr).FontColor(Colors.White).Bold().FontSize(8);
                        });
                        foreach (var r in results)
                        {
                            var bg = r.Status switch { AuditStatus.Pass => "#d1e7dd", AuditStatus.Warn => "#fff3cd", AuditStatus.Fail => "#f8d7da", _ => "#e2e3e5" };
                            table.Cell().Background(bg).Padding(3).Text(r.CheckId.ToString()).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(r.CheckName).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(r.Score).FontSize(8);
                            table.Cell().Background(bg).Padding(3).Text(r.Benchmark).FontSize(7).FontColor("#495057");
                            table.Cell().Background(bg).Padding(3).Text(r.Status.ToString()).Bold().FontSize(8);
                        }
                    });
                    col.Item().PaddingTop(16);

                    foreach (var r in results)
                    {
                        var bc = r.Status switch { AuditStatus.Pass => "#198754", AuditStatus.Warn => "#ffc107", AuditStatus.Fail => "#dc3545", _ => "#6c757d" };
                        col.Item().BorderLeft(3).BorderColor(bc).PaddingLeft(8).PaddingBottom(8).Column(inner =>
                        {
                            inner.Item().Row(row2 =>
                            {
                                row2.RelativeItem().Text($"Check {r.CheckId} — {r.CheckName}").Bold().FontSize(10);
                                row2.ConstantItem(80).AlignRight().Text($"{r.Score}  [{r.Status}]").Bold().FontSize(9);
                            });
                            inner.Item().PaddingBottom(4).Text(r.Summary).FontColor("#6c757d").FontSize(8);
                            if (r.DetailRows.Any())
                            {
                                inner.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); });
                                    foreach (var d in r.DetailRows)
                                    {
                                        table.Cell().Padding(2).Text(d.Label).FontColor("#495057").FontSize(8);
                                        table.Cell().Padding(2).Text(d.Value).Bold().FontSize(8);
                                    }
                                });
                            }
                            if (r.FlagRows.Any())
                            {
                                inner.Item().PaddingTop(4).Text($"Flagged Claims ({r.FlagRows.Count})").Bold().FontSize(8);
                                inner.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(1); });
                                    table.Header(h =>
                                    {
                                        foreach (var hdr in new[] { "Claim #", "Patient", "Date", "Payer", "Detail", "Amount" })
                                            h.Cell().Background("#6c757d").Padding(2).Text(hdr).FontColor(Colors.White).Bold().FontSize(7);
                                    });
                                    foreach (var f in r.FlagRows.Take(20))
                                    {
                                        table.Cell().Padding(2).Text(f.ClaimNo ?? "").FontSize(7);
                                        table.Cell().Padding(2).Text(f.Patient ?? "").FontSize(7);
                                        table.Cell().Padding(2).Text(f.ServiceDate ?? "").FontSize(7);
                                        table.Cell().Padding(2).Text(f.Payer ?? "").FontSize(7);
                                        table.Cell().Padding(2).Text(f.FlagDetail ?? "").FontSize(7);
                                        table.Cell().Padding(2).AlignRight().Text(f.Amount.HasValue ? f.Amount.Value.ToString("C2") : "").FontSize(7);
                                    }
                                    if (r.FlagRows.Count > 20)
                                        table.Cell().ColumnSpan(6).Padding(2)
                                            .Text($"... and {r.FlagRows.Count - 20} more (see Excel export for full list)")
                                            .FontColor("#6c757d").FontSize(7).Italic();
                                });
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ").FontSize(8).FontColor("#6c757d");
                    t.CurrentPageNumber().FontSize(8).FontColor("#6c757d");
                    t.Span(" of ").FontSize(8).FontColor("#6c757d");
                    t.TotalPages().FontSize(8).FontColor("#6c757d");
                });
            });
        }).GeneratePdf();
    }
}
