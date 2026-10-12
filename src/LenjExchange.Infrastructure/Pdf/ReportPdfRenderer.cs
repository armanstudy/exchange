using LenjExchange.Application.Documents;
using LenjExchange.Application.Reporting;
using LenjExchange.Application.Settings;
using LenjExchange.Domain.Monetary;
using LenjExchange.Domain.Shamsi;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

public sealed class ReportPdfRenderer : IReportPdfRenderer
{
    public byte[] RenderStatement(StatementReport report, CompanyHeader h)
    {
        PdfFonts.EnsureInitialized();
        return Document.Create(doc => doc.Page(page =>
        {
            Setup(page, PageSizes.A4.Landscape());
            page.Header().Element(c => PdfCommon.CompanyHeader(c, h, "صورتحساب",
                $"حساب: {report.AccountName} ({report.AccountCode})", report.Range.Describe()));
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Text("خلاصه به تفکیک ارز").Bold().FontColor(h.PrimaryColor);
                col.Item().PaddingBottom(8).Table(t =>
                {
                    t.ColumnsDefinition(cd => { for (var i = 0; i < 6; i++) cd.RelativeColumn(); });
                    foreach (var head in new[] { "ارز", "مانده ابتدای دوره", "جمع بدهکار", "جمع بستانکار", "مانده پایان دوره", "تشخیص" })
                        t.Cell().Element(c => PdfCommon.HeaderCell(c, h)).Text(head);
                    var alt = false;
                    foreach (var s in report.Sections)
                    {
                        Cell(t, h, alt, s.Currency.ToString());
                        Cell(t, h, alt, Amt(s.Opening, s.Currency));
                        Cell(t, h, alt, Amt(s.TotalDebit, s.Currency));
                        Cell(t, h, alt, Amt(s.TotalCredit, s.Currency));
                        Cell(t, h, alt, Amt(s.Closing, s.Currency));
                        Cell(t, h, alt, StatementCurrencySection.Side(s.Closing));
                        alt = !alt;
                    }
                });

                foreach (var s in report.Sections)
                {
                    col.Item().PaddingTop(8).Text($"ارز {s.Currency} — مانده ابتدای دوره: {Amt(s.Opening, s.Currency)} ({StatementCurrencySection.Side(s.Opening)})")
                        .Bold().FontColor(h.PrimaryColor);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(80); cd.ConstantColumn(62); cd.RelativeColumn(5); cd.ConstantColumn(38);
                            cd.RelativeColumn(1.6f); cd.RelativeColumn(1.6f); cd.RelativeColumn(1.8f);
                        });
                        t.Header(hd =>
                        {
                            foreach (var head in new[] { "شماره سند", "تاریخ", "شرح", "ارز", "بدهکار", "بستانکار", "مانده" })
                                hd.Cell().Element(c => PdfCommon.HeaderCell(c, h)).Text(head);
                        });
                        var alt = false;
                        foreach (var r in s.Rows)
                        {
                            Cell(t, h, alt, r.EntryNumber);
                            Cell(t, h, alt, r.ShamsiDate);
                            Cell(t, h, alt, r.Description);
                            Cell(t, h, alt, r.Currency.ToString());
                            Cell(t, h, alt, r.Debit == 0 ? "" : Amt(r.Debit, r.Currency));
                            Cell(t, h, alt, r.Credit == 0 ? "" : Amt(r.Credit, r.Currency));
                            Cell(t, h, alt, $"{Amt(Math.Abs(r.Balance), r.Currency)} {SideShort(r.Balance)}");
                            alt = !alt;
                        }
                    });
                    col.Item().PaddingTop(2).Text(
                        $"جمع بدهکار: {Amt(s.TotalDebit, s.Currency)}   جمع بستانکار: {Amt(s.TotalCredit, s.Currency)}   مانده پایان دوره: {Amt(Math.Abs(s.Closing), s.Currency)} ({StatementCurrencySection.Side(s.Closing)})")
                        .FontSize(9).Bold();
                }
                if (report.Sections.Count == 0)
                    col.Item().PaddingTop(20).AlignCenter().Text("در این بازه گردشی وجود ندارد.").FontColor(h.MutedTextColor);
            });
            page.Footer().Element(c => PdfCommon.PageNumbers(c, h));
        })).GeneratePdf();
    }

    public byte[] RenderAllCustomers(AllCustomersReport report, CompanyHeader h)
    {
        PdfFonts.EnsureInitialized();
        return Document.Create(doc => doc.Page(page =>
        {
            Setup(page, PageSizes.A4.Landscape());
            page.Header().Element(c => PdfCommon.CompanyHeader(c, h, "گزارش مانده همه مشتریان", null, report.Range.Describe()));
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(60); cd.RelativeColumn(3); cd.ConstantColumn(40); cd.ConstantColumn(45);
                        cd.RelativeColumn(1.6f); cd.RelativeColumn(1.6f); cd.RelativeColumn(1.6f); cd.ConstantColumn(50); cd.ConstantColumn(62);
                    });
                    t.Header(hd =>
                    {
                        foreach (var head in new[] { "کد", "نام مشتری", "ارز", "تعداد", "جمع دریافتی (بستانکار)", "جمع پرداختی (بدهکار)", "مانده", "تشخیص", "آخرین تراکنش" })
                            hd.Cell().Element(c => PdfCommon.HeaderCell(c, h)).Text(head);
                    });
                    var alt = false;
                    foreach (var r in report.Rows)
                    {
                        Cell(t, h, alt, r.Code);
                        Cell(t, h, alt, r.FullName);
                        Cell(t, h, alt, r.Currency.ToString());
                        Cell(t, h, alt, r.TransactionCount.ToString());
                        Cell(t, h, alt, Amt(r.TotalReceived, r.Currency));
                        Cell(t, h, alt, Amt(r.TotalPaid, r.Currency));
                        Cell(t, h, alt, Amt(Math.Abs(r.Balance), r.Currency));
                        Cell(t, h, alt, r.Side);
                        Cell(t, h, alt, r.LastTransactionUtc is { } d ? ShamsiDate.Format(d.ToLocalTime()) : "");
                        alt = !alt;
                    }
                });
                col.Item().PaddingTop(10).Text("جمع به تفکیک ارز (ارزهای مختلف هرگز با هم جمع نمی‌شوند)").Bold().FontColor(h.PrimaryColor);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { for (var i = 0; i < 4; i++) cd.RelativeColumn(); });
                    foreach (var head in new[] { "ارز", "جمع دریافتی", "جمع پرداختی", "مانده خالص" })
                        t.Cell().Element(c => PdfCommon.HeaderCell(c, h)).Text(head);
                    var alt = false;
                    foreach (var (cur, rec, paid, bal) in report.Totals)
                    {
                        Cell(t, h, alt, cur.ToString());
                        Cell(t, h, alt, Amt(rec, cur));
                        Cell(t, h, alt, Amt(paid, cur));
                        Cell(t, h, alt, $"{Amt(Math.Abs(bal), cur)} {SideShort(bal)}");
                        alt = !alt;
                    }
                });
            });
            page.Footer().Element(c => PdfCommon.PageNumbers(c, h));
        })).GeneratePdf();
    }

    private static void Setup(PageDescriptor page, PageSize size)
    {
        page.Size(size);
        page.Margin(24);
        page.ContentFromRightToLeft();
        page.DefaultTextStyle(x => x.FontFamily(PdfFonts.Family).FontSize(9));
    }

    private static void Cell(TableDescriptor t, CompanyHeader h, bool alt, string text) =>
        t.Cell().Element(c => PdfCommon.BodyCell(c, h, alt)).Text(text);

    private static string Amt(decimal v, CurrencyCode c) => AmountFormatter.Format(v, c);

    private static string SideShort(decimal v) => v > 0 ? "بد" : v < 0 ? "بس" : "";
}
