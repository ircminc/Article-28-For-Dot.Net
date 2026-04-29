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

    // ===================================================================
    // CMS-1500 form-shaped PDF (professional claim layout).
    //
    // Recognizable as the standard CMS-1500 form — 33 numbered fields in
    // their conventional positions — but rendered from scratch in QuestPDF
    // rather than overlaying an official PDF template. Suitable for
    // internal review, comparison, and audit; NOT pixel-accurate enough
    // to be OCR-scanned by clearinghouses. Path-2 (pixel-perfect) or
    // Path-3 (fillable template) would be future upgrades.
    // ===================================================================
    public byte[] BuildCms1500Pdf(ParsedClaim claim, ApgResultRecord? apg, List<APGLineResult> apgLines)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dx = string.IsNullOrEmpty(claim.PrincipalDiagnosis)
            ? new List<string>()
            : new List<string> { claim.PrincipalDiagnosis };
        if (!string.IsNullOrEmpty(claim.OtherDiagnosesJson))
        {
            try
            {
                var others = JsonSerializer.Deserialize<List<string>>(claim.OtherDiagnosesJson) ?? new();
                dx.AddRange(others);
            }
            catch { /* tolerate bad JSON */ }
        }

        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.Letter);
                p.Margin(20);
                p.DefaultTextStyle(t => t.FontSize(7));   // CMS-1500 prints in tight, 7-8pt-ish text

                p.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("HEALTH INSURANCE CLAIM FORM").FontSize(11).Bold();
                        r.ConstantItem(150).AlignRight().Text("APPROVED OMB-0938-1197 FORM CMS-1500 (02-12)").FontSize(7);
                    });
                    col.Item().PaddingTop(2).LineHorizontal(0.5f);
                });

                p.Content().PaddingVertical(6).Column(body =>
                {
                    body.Spacing(3);

                    // ---------------- Box 1 — Carrier (top, full-width) ----------------
                    body.Item().Border(0.5f).Padding(4).Column(c1 =>
                    {
                        c1.Item().Text("CARRIER").FontSize(7).FontColor(Colors.Grey.Darken1);
                        c1.Item().Text(claim.PayerName ?? "").FontSize(9).SemiBold();
                    });

                    // ---------------- Boxes 1a-13 — Patient/Insured info grid -----------
                    body.Item().Row(r =>
                    {
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("1.  INSURANCE TYPE").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ClaimFilingIndicator switch
                            {
                                "MC" => "[X] MEDICARE",
                                "MB" => "[X] MEDICAID",
                                "CI" => "[X] CHAMPUS",
                                _    => "[ ] " + (claim.ClaimFilingIndicator ?? "OTHER"),
                            });
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("1a. INSURED'S I.D. NUMBER").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PatientId ?? "").SemiBold();
                        });
                    });

                    body.Item().Row(r =>
                    {
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("2.  PATIENT'S NAME (Last, First, Middle)").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PatientName ?? "").SemiBold();
                        });
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("3.  PATIENT'S BIRTH DATE   SEX").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().MinHeight(10).Text(" ");
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("4.  INSURED'S NAME (Last, First, Middle)").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PatientName ?? "");
                        });
                    });

                    // ---------------- Boxes 14-16 dates / 17 referring provider ----------
                    body.Item().Row(r =>
                    {
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("14. DATE OF CURRENT ILLNESS, INJURY, or PREGNANCY").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.DateOfService?.ToString("MM/dd/yyyy") ?? "");
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("17. NAME OF REFERRING PROVIDER OR OTHER SOURCE").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderName ?? "");
                        });
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("17b. NPI").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderNpi ?? "");
                        });
                    });

                    // ---------------- Box 21 — Diagnosis codes (12 slots, A-L) -----------
                    body.Item().Border(0.5f).Padding(4).Column(c1 =>
                    {
                        c1.Item().Text("21. DIAGNOSIS OR NATURE OF ILLNESS OR INJURY  (Relate A-L to service line below — ICD Ind. [X] 0)").FontSize(6).FontColor(Colors.Grey.Darken1);
                        c1.Item().PaddingTop(2).Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                for (int i = 0; i < 4; i++) cd.RelativeColumn();
                            });
                            // 3 rows × 4 cols = 12 dx slots
                            for (int row = 0; row < 3; row++)
                            {
                                for (int col = 0; col < 4; col++)
                                {
                                    var idx = row * 4 + col;
                                    var letter = ((char)('A' + idx)).ToString();
                                    var code = idx < dx.Count ? dx[idx] : "";
                                    t.Cell().Border(0.3f).Padding(3).Column(c2 =>
                                    {
                                        c2.Item().Text($"{letter}.").FontSize(6).FontColor(Colors.Grey.Darken1);
                                        c2.Item().Text(code).FontSize(8).SemiBold();
                                    });
                                }
                            }
                        });
                    });

                    // ---------------- Box 24 — Service line table -----------------------
                    body.Item().Border(0.5f).PaddingTop(2).Column(c1 =>
                    {
                        c1.Item().Padding(2).Text("24.  SERVICE LINES").FontSize(6).FontColor(Colors.Grey.Darken1);
                        c1.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.ConstantColumn(70);   // A: From / To dates
                                cd.ConstantColumn(30);   // B: POS
                                cd.ConstantColumn(20);   // C: EMG
                                cd.RelativeColumn(2);    // D: Procedure / Modifiers
                                cd.ConstantColumn(40);   // E: Dx pointer
                                cd.RelativeColumn(1);    // F: $ Charges
                                cd.ConstantColumn(30);   // G: Days/Units
                                cd.ConstantColumn(35);   // H: EPSDT
                                cd.ConstantColumn(40);   // I: ID. Qual.
                                cd.RelativeColumn();     // J: Rendering NPI
                            });
                            t.Header(h =>
                            {
                                foreach (var lbl in new[] {
                                    "A. DATE(S) OF SERVICE", "B. POS", "C. EMG",
                                    "D. CPT/HCPCS · MODIFIERS", "E. DX PTR",
                                    "F. $ CHARGES", "G. UNITS", "H. EPSDT",
                                    "I. ID. QUAL", "J. RENDERING PROVIDER ID #",
                                })
                                {
                                    h.Cell().Background(Colors.Grey.Lighten4).Border(0.3f).Padding(2)
                                        .Text(lbl).FontSize(5).SemiBold();
                                }
                            });
                            // Up to 6 service lines; pad blank rows to keep layout
                            var lines = claim.ServiceLines.OrderBy(x => x.LineSeq).ToList();
                            for (int i = 0; i < Math.Max(6, lines.Count); i++)
                            {
                                var sl = i < lines.Count ? lines[i] : null;
                                var mods = sl is null || string.IsNullOrEmpty(sl.ModifiersJson)
                                    ? new List<string>()
                                    : JsonSerializer.Deserialize<List<string>>(sl.ModifiersJson) ?? new();
                                var modsText = mods.Count > 0 ? "  " + string.Join(" ", mods) : "";

                                t.Cell().Border(0.3f).Padding(3).Text(sl?.DateOfService?.ToString("MM/dd/yyyy") ?? "");
                                t.Cell().Border(0.3f).Padding(3).Text(""); // POS (not parsed yet)
                                t.Cell().Border(0.3f).Padding(3).Text("");
                                t.Cell().Border(0.3f).Padding(3).Text((sl?.ProcedureCode ?? "") + modsText);
                                t.Cell().Border(0.3f).Padding(3).Text(dx.Count > 0 ? "A" : "");
                                t.Cell().Border(0.3f).Padding(3).AlignRight().Text(sl is null ? "" : sl.BilledAmount.ToString("C2"));
                                t.Cell().Border(0.3f).Padding(3).AlignRight().Text(sl?.Units.ToString() ?? "");
                                t.Cell().Border(0.3f).Padding(3).Text("");
                                t.Cell().Border(0.3f).Padding(3).Text("NPI");
                                t.Cell().Border(0.3f).Padding(3).Text(claim.ProviderNpi ?? "");
                            }
                        });
                    });

                    // ---------------- Boxes 25-33 — Tax ID / charges / billing provider -
                    body.Item().Row(r =>
                    {
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("25. FEDERAL TAX I.D. NUMBER").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("");
                        });
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("26. PATIENT'S ACCOUNT NO.").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ClaimId);
                        });
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("28. TOTAL CHARGE").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().AlignRight().Text(claim.BilledAmount.ToString("C2")).SemiBold();
                        });
                        r.RelativeItem().Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("29. AMOUNT PAID").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().AlignRight().Text(claim.PaidAmount.ToString("C2")).SemiBold();
                        });
                    });

                    body.Item().Row(r =>
                    {
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("32. SERVICE FACILITY LOCATION INFORMATION").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderName ?? "");
                            c1.Item().Text(claim.ProviderNpi ?? "").FontColor(Colors.Grey.Darken2);
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(4).Column(c1 =>
                        {
                            c1.Item().Text("33. BILLING PROVIDER INFO & PH #").FontSize(6).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderName ?? "");
                            c1.Item().Text("NPI: " + (claim.ProviderNpi ?? "")).FontColor(Colors.Grey.Darken2);
                        });
                    });
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("CMS-1500 form-shaped reproduction · ").FontSize(7).FontColor(Colors.Grey.Medium);
                    t.Span("APG Rate Analyzer · ").FontSize(7).FontColor(Colors.Grey.Medium);
                    t.Span($"generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(7).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    // ===================================================================
    // UB-04 form-shaped PDF (institutional claim layout).
    //
    // CMS-1450 (UB-04) layout — Form Locators FL1 through FL81 in their
    // traditional positions. Recognizable as a UB-04 but not pixel-
    // accurate. Same caveats as CMS-1500 above.
    // ===================================================================
    public byte[] BuildUb04Pdf(ParsedClaim claim, ApgResultRecord? apg, List<APGLineResult> apgLines)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var dx = new List<string>();
        if (!string.IsNullOrEmpty(claim.PrincipalDiagnosis)) dx.Add(claim.PrincipalDiagnosis);
        if (!string.IsNullOrEmpty(claim.OtherDiagnosesJson))
        {
            try
            {
                var others = JsonSerializer.Deserialize<List<string>>(claim.OtherDiagnosesJson) ?? new();
                dx.AddRange(others);
            }
            catch { /* tolerate */ }
        }

        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.Letter.Landscape());
                p.Margin(15);
                p.DefaultTextStyle(t => t.FontSize(6));

                p.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("UB-04 / CMS-1450 — INSTITUTIONAL CLAIM").FontSize(10).Bold();
                        r.ConstantItem(150).AlignRight().Text("APPROVED OMB NO. 0938-0997").FontSize(7);
                    });
                    col.Item().PaddingTop(2).LineHorizontal(0.5f);
                });

                p.Content().PaddingVertical(4).Column(body =>
                {
                    body.Spacing(2);

                    // ---------------- Top row: FL1 provider / FL3-7 patient + bill ----
                    body.Item().Row(r =>
                    {
                        r.RelativeItem(3).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("1. BILLING PROVIDER NAME, ADDRESS, TELEPHONE").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderName ?? "").SemiBold();
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("2. PAY-TO PROVIDER").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderName ?? "");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("3a. PAT. CNTL #").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ClaimId);
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("4. TYPE OF BILL").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.FileType);
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("5. FED TAX NO.").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("");
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("6. STATEMENT COVERS PERIOD  FROM | THROUGH").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.DateOfService?.ToString("MM/dd/yyyy") ?? "");
                        });
                    });

                    // ---------------- FL8 patient / FL10-17 admission --------------------
                    body.Item().Row(r =>
                    {
                        r.RelativeItem(3).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("8a. PATIENT NAME").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PatientName ?? "").SemiBold();
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("9. PATIENT ADDRESS").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("10. BIRTHDATE").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("11. SEX").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("12. ADMIT DATE").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.DateOfService?.ToString("MMddyy") ?? "");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("17. STAT").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ClaimStatus ?? "");
                        });
                    });

                    // ---------------- FL42-49 service line table (the meat) ---------------
                    body.Item().PaddingTop(2).Border(0.5f).Column(c1 =>
                    {
                        c1.Item().Padding(2).Text("42-49.  SERVICE LINES").FontSize(5).FontColor(Colors.Grey.Darken1);
                        c1.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.ConstantColumn(40);   // 42 Rev cd
                                cd.RelativeColumn(2);    // 43 Description
                                cd.RelativeColumn();     // 44 HCPCS / Rates
                                cd.ConstantColumn(50);   // 45 Service date
                                cd.ConstantColumn(40);   // 46 Units
                                cd.ConstantColumn(70);   // 47 Total charges
                                cd.ConstantColumn(70);   // 48 Non-cov charges
                                cd.ConstantColumn(50);   // 49 (reserved)
                            });
                            t.Header(h =>
                            {
                                foreach (var lbl in new[] {
                                    "42 REV CD", "43 DESCRIPTION", "44 HCPCS / RATES",
                                    "45 SERV DATE", "46 SERV UNITS",
                                    "47 TOTAL CHARGES", "48 NON-COV", "49",
                                })
                                {
                                    h.Cell().Background(Colors.Grey.Lighten4).Border(0.3f).Padding(2)
                                        .Text(lbl).FontSize(5).SemiBold();
                                }
                            });
                            var lines = claim.ServiceLines.OrderBy(x => x.LineSeq).ToList();
                            for (int i = 0; i < Math.Max(8, lines.Count); i++)
                            {
                                var sl = i < lines.Count ? lines[i] : null;
                                var mods = sl is null || string.IsNullOrEmpty(sl.ModifiersJson)
                                    ? new List<string>()
                                    : JsonSerializer.Deserialize<List<string>>(sl.ModifiersJson) ?? new();
                                var modsText = mods.Count > 0 ? " " + string.Join(" ", mods) : "";

                                t.Cell().Border(0.3f).Padding(3).Text(sl?.RevenueCode ?? "");
                                t.Cell().Border(0.3f).Padding(3).Text(""); // description (could pull from HCPCS table)
                                t.Cell().Border(0.3f).Padding(3).Text((sl?.ProcedureCode ?? "") + modsText);
                                t.Cell().Border(0.3f).Padding(3).Text(sl?.DateOfService?.ToString("MMddyy") ?? "");
                                t.Cell().Border(0.3f).Padding(3).AlignRight().Text(sl?.Units.ToString() ?? "");
                                t.Cell().Border(0.3f).Padding(3).AlignRight().Text(sl is null ? "" : sl.BilledAmount.ToString("F2"));
                                t.Cell().Border(0.3f).Padding(3).Text("");
                                t.Cell().Border(0.3f).Padding(3).Text("");
                            }

                            // Totals row at the bottom
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("PAGE TOTALS").SemiBold();
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).AlignRight().Text(claim.BilledAmount.ToString("F2")).SemiBold();
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                            t.Cell().Border(0.3f).Padding(3).Background(Colors.Grey.Lighten4).Text("");
                        });
                    });

                    // ---------------- FL50-65 payer / FL66-67 dx ------------------------
                    body.Item().Row(r =>
                    {
                        r.RelativeItem(2).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("50. PAYER NAME").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PayerName ?? "");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("56. NPI").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.ProviderNpi ?? "");
                        });
                        r.RelativeItem().Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("60. INSURED'S UNIQUE ID").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text(claim.PatientId ?? "");
                        });
                        r.RelativeItem(2).Border(0.5f).Padding(3).Column(c1 =>
                        {
                            c1.Item().Text("66. ICD IND.   67. PRINCIPAL DIAGNOSIS").FontSize(5).FontColor(Colors.Grey.Darken1);
                            c1.Item().Text("0  " + (claim.PrincipalDiagnosis ?? "")).SemiBold();
                        });
                    });

                    // ---------------- Other dx (FL67A-67Q) -----------------------------
                    body.Item().Border(0.5f).Padding(3).Column(c1 =>
                    {
                        c1.Item().Text("67A-67Q. OTHER DIAGNOSIS CODES").FontSize(5).FontColor(Colors.Grey.Darken1);
                        c1.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                for (int i = 0; i < 9; i++) cd.RelativeColumn();
                            });
                            // Skip the principal (already on its own line); include up to 17 others
                            var others = dx.Skip(1).Take(17).ToList();
                            for (int i = 0; i < 18; i++)
                            {
                                var letter = i < 17
                                    ? "67" + ((char)('A' + i)).ToString()
                                    : "";
                                var code = i < others.Count ? others[i] : "";
                                t.Cell().Border(0.3f).Padding(2).Column(c2 =>
                                {
                                    c2.Item().Text(letter).FontSize(4).FontColor(Colors.Grey.Darken1);
                                    c2.Item().Text(code).FontSize(7);
                                });
                            }
                        });
                    });
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("UB-04 form-shaped reproduction · ").FontSize(6).FontColor(Colors.Grey.Medium);
                    t.Span("APG Rate Analyzer · ").FontSize(6).FontColor(Colors.Grey.Medium);
                    t.Span($"generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(6).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return doc.GeneratePdf();
    }

    // ===================================================================
    // Full-fidelity data export (for data transitions / migration).
    // Each entity → its own flat sheet, every column exposed, joinable
    // on ClaimId / ClaimIdFk. Reads the user's checkbox selection so
    // you can include / exclude any sheet.
    // ===================================================================

    public byte[] BuildFullDataXlsx(
        IReadOnlyList<ParsedClaim> claims,
        DataExportViewModel options)
    {
        using var wb = new XLWorkbook();
        var headerFill = XLColor.FromHtml("#E8F1FE");

        // ---------- Claims sheet ----------
        if (options.IncludeClaims)
        {
            var ws = wb.AddWorksheet("Claims");
            var hdr = new[]
            {
                "Id", "FileId", "FileType", "ClaimId", "ClaimStatus",
                "PayerName", "PayerId", "ProviderName", "ProviderNpi",
                "PatientName", "PatientId", "DateOfService",
                "BilledAmount", "AllowedAmount", "PaidAmount", "PatientResponsibility",
                "ClaimFilingIndicator", "PrincipalDiagnosis", "OtherDiagnoses",
                "LinkedClaimIdFk", "CreatedAt",
            };
            WriteHeader(ws, hdr, headerFill);
            int r = 2;
            foreach (var c in claims)
            {
                ws.Cell(r, 1).Value  = c.Id;
                ws.Cell(r, 2).Value  = c.FileId;
                ws.Cell(r, 3).Value  = c.FileType;
                ws.Cell(r, 4).Value  = c.ClaimId;
                ws.Cell(r, 5).Value  = c.ClaimStatus ?? "";
                ws.Cell(r, 6).Value  = c.PayerName ?? "";
                ws.Cell(r, 7).Value  = c.PayerId ?? "";
                ws.Cell(r, 8).Value  = c.ProviderName ?? "";
                ws.Cell(r, 9).Value  = c.ProviderNpi ?? "";
                ws.Cell(r, 10).Value = c.PatientName ?? "";
                ws.Cell(r, 11).Value = c.PatientId ?? "";
                ws.Cell(r, 12).Value = c.DateOfService?.ToDateTime(TimeOnly.MinValue);
                ws.Cell(r, 13).Value = c.BilledAmount;
                ws.Cell(r, 14).Value = c.AllowedAmount;
                ws.Cell(r, 15).Value = c.PaidAmount;
                ws.Cell(r, 16).Value = c.PatientResponsibility;
                ws.Cell(r, 17).Value = c.ClaimFilingIndicator ?? "";
                ws.Cell(r, 18).Value = c.PrincipalDiagnosis ?? "";
                ws.Cell(r, 19).Value = c.OtherDiagnosesJson ?? "";
                ws.Cell(r, 20).Value = c.LinkedClaimIdFk;
                ws.Cell(r, 21).Value = c.CreatedAt;
                r++;
            }
            ws.Range(2, 12, Math.Max(2, r - 1), 12).Style.NumberFormat.Format = "yyyy-mm-dd";
            ws.Range(2, 13, Math.Max(2, r - 1), 16).Style.NumberFormat.Format = "$#,##0.00";
            ws.Range(2, 21, Math.Max(2, r - 1), 21).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm:ss";
            AdjustAndCap(ws);
        }

        // ---------- ServiceLines sheet ----------
        if (options.IncludeServiceLines)
        {
            var ws = wb.AddWorksheet("ServiceLines");
            var hdr = new[]
            {
                "Id", "ClaimIdFk", "ClaimId", "FileType", "LineSeq",
                "ProcedureCode", "Modifiers", "RevenueCode",
                "BilledAmount", "AllowedAmount", "PaidAmount",
                "Units", "DateOfService",
            };
            WriteHeader(ws, hdr, headerFill);
            int r = 2;
            foreach (var c in claims)
            {
                foreach (var sl in c.ServiceLines)
                {
                    var mods = string.IsNullOrEmpty(sl.ModifiersJson)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(sl.ModifiersJson) ?? new();
                    ws.Cell(r, 1).Value  = sl.Id;
                    ws.Cell(r, 2).Value  = sl.ClaimIdFk;
                    ws.Cell(r, 3).Value  = c.ClaimId;
                    ws.Cell(r, 4).Value  = c.FileType;
                    ws.Cell(r, 5).Value  = sl.LineSeq;
                    ws.Cell(r, 6).Value  = sl.ProcedureCode;
                    ws.Cell(r, 7).Value  = string.Join(", ", mods);
                    ws.Cell(r, 8).Value  = sl.RevenueCode ?? "";
                    ws.Cell(r, 9).Value  = sl.BilledAmount;
                    ws.Cell(r, 10).Value = sl.AllowedAmount;
                    ws.Cell(r, 11).Value = sl.PaidAmount;
                    ws.Cell(r, 12).Value = sl.Units;
                    ws.Cell(r, 13).Value = sl.DateOfService?.ToDateTime(TimeOnly.MinValue);
                    r++;
                }
            }
            ws.Range(2, 9, Math.Max(2, r - 1), 11).Style.NumberFormat.Format = "$#,##0.00";
            ws.Range(2, 13, Math.Max(2, r - 1), 13).Style.NumberFormat.Format = "yyyy-mm-dd";
            AdjustAndCap(ws);
        }

        // ---------- Adjustments sheet ----------
        if (options.IncludeAdjustments)
        {
            var ws = wb.AddWorksheet("Adjustments");
            var hdr = new[]
            {
                "Id", "ClaimIdFk", "ClaimId", "Scope", "LineSeq",
                "GroupCode", "ReasonCode", "Amount", "Quantity",
            };
            WriteHeader(ws, hdr, headerFill);
            int r = 2;
            foreach (var c in claims)
            {
                foreach (var adj in c.Adjustments)
                {
                    ws.Cell(r, 1).Value = adj.Id;
                    ws.Cell(r, 2).Value = adj.ClaimIdFk;
                    ws.Cell(r, 3).Value = c.ClaimId;
                    ws.Cell(r, 4).Value = adj.LineSeq.HasValue ? "Line" : "Claim";
                    ws.Cell(r, 5).Value = adj.LineSeq;
                    ws.Cell(r, 6).Value = adj.GroupCode;
                    ws.Cell(r, 7).Value = adj.ReasonCode;
                    ws.Cell(r, 8).Value = adj.Amount;
                    ws.Cell(r, 9).Value = adj.Quantity;
                    r++;
                }
            }
            ws.Range(2, 8, Math.Max(2, r - 1), 8).Style.NumberFormat.Format = "$#,##0.00";
            AdjustAndCap(ws);
        }

        // ---------- ApgResults sheet ----------
        if (options.IncludeApgResults)
        {
            var ws = wb.AddWorksheet("ApgResults");
            var hdr = new[]
            {
                "ClaimIdFk", "ClaimId", "PeerGroup", "Region", "BaseRateApplied",
                "CorrectApgPayment", "ActualPaid", "Variance", "CompressionPct",
                "Underpaid", "Overpaid", "DiscountingApplied", "U6Applied",
                "CapitalApplied", "CalculatedAt",
            };
            WriteHeader(ws, hdr, headerFill);
            int r = 2;
            foreach (var c in claims)
            {
                if (c.ApgResult is null) continue;
                var a = c.ApgResult;
                ws.Cell(r, 1).Value  = a.ClaimIdFk;
                ws.Cell(r, 2).Value  = c.ClaimId;
                ws.Cell(r, 3).Value  = a.PeerGroup;
                ws.Cell(r, 4).Value  = a.Region;
                ws.Cell(r, 5).Value  = a.BaseRateApplied;
                ws.Cell(r, 6).Value  = a.CorrectApgPayment;
                ws.Cell(r, 7).Value  = a.ActualPaid;
                ws.Cell(r, 8).Value  = a.Variance;
                ws.Cell(r, 9).Value  = a.CompressionPct;
                ws.Cell(r, 10).Value = a.Underpaid;
                ws.Cell(r, 11).Value = a.Overpaid;
                ws.Cell(r, 12).Value = a.DiscountingApplied;
                ws.Cell(r, 13).Value = a.U6Applied;
                ws.Cell(r, 14).Value = a.CapitalApplied;
                ws.Cell(r, 15).Value = a.CalculatedAt;
                r++;
            }
            ws.Range(2, 5, Math.Max(2, r - 1), 8).Style.NumberFormat.Format = "$#,##0.00";
            ws.Range(2, 9, Math.Max(2, r - 1), 9).Style.NumberFormat.Format = "0.0000";
            ws.Range(2, 15, Math.Max(2, r - 1), 15).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm:ss";
            AdjustAndCap(ws);
        }

        // ---------- ApgLineDetails sheet (flatten the JSON) ----------
        if (options.IncludeApgLineDetails)
        {
            var ws = wb.AddWorksheet("ApgLineDetails");
            var hdr = new[]
            {
                "ClaimIdFk", "ClaimId", "LineSeq", "ProcedureCode", "Modifiers",
                "Eapg", "EapgDesc", "EapgType", "EapgTypeRaw", "EapgCategory",
                "Weight", "BaseRate", "ExpectedPayment", "ActualPaid", "Variance",
                "Packaged", "Discounted", "U6Applied", "Denied",
                "FeeScheduled", "PxWeightApplied", "Notes",
            };
            WriteHeader(ws, hdr, headerFill);
            int r = 2;
            foreach (var c in claims)
            {
                if (c.ApgResult is null || string.IsNullOrEmpty(c.ApgResult.LineDetailsJson)) continue;
                List<APGLineResult>? lines;
                try { lines = JsonSerializer.Deserialize<List<APGLineResult>>(c.ApgResult.LineDetailsJson); }
                catch { continue; }
                if (lines is null) continue;

                foreach (var ld in lines)
                {
                    ws.Cell(r, 1).Value  = c.ApgResult.ClaimIdFk;
                    ws.Cell(r, 2).Value  = c.ClaimId;
                    ws.Cell(r, 3).Value  = ld.LineSeq;
                    ws.Cell(r, 4).Value  = ld.ProcedureCode;
                    ws.Cell(r, 5).Value  = string.Join(", ", ld.Modifiers);
                    ws.Cell(r, 6).Value  = ld.Eapg;
                    ws.Cell(r, 7).Value  = ld.EapgDesc ?? "";
                    ws.Cell(r, 8).Value  = ld.EapgType.ToString();
                    ws.Cell(r, 9).Value  = ld.EapgTypeRaw ?? "";
                    ws.Cell(r, 10).Value = ld.EapgCategory ?? "";
                    ws.Cell(r, 11).Value = ld.Weight ?? 0;
                    ws.Cell(r, 12).Value = ld.BaseRate;
                    ws.Cell(r, 13).Value = ld.ExpectedPayment;
                    ws.Cell(r, 14).Value = ld.ActualPaid;
                    ws.Cell(r, 15).Value = ld.Variance;
                    ws.Cell(r, 16).Value = ld.Packaged;
                    ws.Cell(r, 17).Value = ld.Discounted;
                    ws.Cell(r, 18).Value = ld.U6Applied;
                    ws.Cell(r, 19).Value = ld.Denied;
                    ws.Cell(r, 20).Value = ld.FeeScheduled;
                    ws.Cell(r, 21).Value = ld.PxWeightApplied;
                    ws.Cell(r, 22).Value = string.Join(" | ", ld.Notes);
                    r++;
                }
            }
            ws.Range(2, 11, Math.Max(2, r - 1), 11).Style.NumberFormat.Format = "0.000000";
            ws.Range(2, 12, Math.Max(2, r - 1), 15).Style.NumberFormat.Format = "$#,##0.00";
            AdjustAndCap(ws);
        }

        // Edge case: nothing selected (defensive — controller should also catch this)
        if (wb.Worksheets.Count == 0)
            wb.AddWorksheet("Empty").Cell(1, 1).Value = "No sheets selected.";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteHeader(IXLWorksheet ws, string[] headers, XLColor fill)
    {
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        var range = ws.Range(1, 1, 1, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = fill;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
    }

    private static void AdjustAndCap(IXLWorksheet ws)
    {
        ws.Columns().AdjustToContents();
        foreach (var col in ws.Columns())
            if (col.Width > 50) col.Width = 50;
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
