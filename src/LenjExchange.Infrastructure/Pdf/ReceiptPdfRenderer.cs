using LenjExchange.Domain.Text;
using LenjExchange.Application.Documents;
using LenjExchange.Domain.Documents;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

/// <summary>Customer receipt (رسید دریافت وجه): header + exactly five customer-facing fields. RTL, A4 or A5.</summary>
public sealed class ReceiptPdfRenderer : IReceiptPdfRenderer
{
    public byte[] Render(ReceiptPrintModel model, PaperSize size)
    {
        PdfFonts.EnsureInitialized();
        var h = model.Header;
        var fields = new List<(string Label, string Value)>
        {
            ("نام و نام خانوادگی پرداخت‌کننده", model.CustomerFullName),
            ("تاریخ", model.ShamsiDate),
            ("مبلغ دریافتی", model.AmountText),
            ("نرخ تبدیل", model.ConversionRateText ?? "—"),
            ("نام گیرنده نهایی", model.FinalRecipientName ?? "—"),
        };

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(size == PaperSize.A5 ? PageSizes.A5 : PageSizes.A4);
            page.Margin(size == PaperSize.A5 ? 18 : 30);
            page.ContentFromRightToLeft();
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(PdfFonts.Family).FontSize(size == PaperSize.A5 ? 10 : 11));

            page.Header().Element(c => PdfCommon.CompanyHeader(c, h, "رسید دریافت وجه", $"شماره رسید: {model.ReceiptNumber}", $"تاریخ: {model.ShamsiDate}"));

            page.Content().PaddingTop(16).Column(col =>
            {
                col.Item().Border(1).BorderColor(h.LightColor).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2);
                        cd.RelativeColumn(3);
                    });
                    var alt = false;
                    foreach (var (label, value) in fields)
                    {
                        t.Cell().Background(h.LightColor).PaddingVertical(8).PaddingHorizontal(8)
                            .Text(label).Bold().FontColor(h.PrimaryColor);
                        t.Cell().Background(alt ? h.BackgroundColor : Colors.White).PaddingVertical(8).PaddingHorizontal(8)
                            .Text(BidiText.IsolateLatinRuns(value));
                        alt = !alt;
                    }
                });
            });

            page.Footer().AlignCenter().Text(model.ReceiptNumber).FontSize(7).FontColor(h.MutedTextColor);
        })).GeneratePdf();
    }
}
