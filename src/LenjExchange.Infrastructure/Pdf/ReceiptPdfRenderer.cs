using LenjExchange.Domain.Text;
using LenjExchange.Application.Documents;
using LenjExchange.Domain.Documents;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

/// <summary>
/// Customer receipt (رسید دریافت وجه) laid out like References/Receipt-Template.docx: letterhead, navy title and
/// section bars, label/value grid, signature block and footer. Content is limited to the five customer-facing
/// fields (payer, date, amount, conversion rate, final recipient) plus the receipt number.
/// </summary>
public sealed class ReceiptPdfRenderer : IReceiptPdfRenderer
{
    public byte[] Render(ReceiptPrintModel model, PaperSize size)
    {
        PdfFonts.EnsureInitialized();
        var h = model.Header;
        var a5 = size == PaperSize.A5;
        var scale = a5 ? 0.8f : 1f;

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            if (a5) page.Margin(10, Unit.Millimetre);
            else
            {
                page.MarginTop(18, Unit.Millimetre);
                page.MarginHorizontal(20, Unit.Millimetre);
                page.MarginBottom(16, Unit.Millimetre);
            }
            page.ContentFromRightToLeft();
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(PdfFonts.Family).FontSize(11.5f * scale).FontColor(h.TextColor));

            page.Header().Element(c => PdfCommon.CompanyHeader(c, h, "رسید دریافت وجه", null, null, scale));

            page.Content().PaddingTop(4).Column(col =>
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(1500);
                        cd.RelativeColumn(3319);
                        cd.RelativeColumn(1500);
                        cd.RelativeColumn(3319);
                    });
                    Label(t.Cell(), "شماره رسید");
                    Value(t.Cell(), model.ReceiptNumber, center: true);
                    Label(t.Cell(), "تاریخ");
                    Value(t.Cell(), model.ShamsiDate, center: true);
                });

                col.Item().Element(c => PdfCommon.SectionBar(c, h, "مشخصات پرداخت‌کننده", scale));
                FieldTable(col.Item(), ("نام شخص / شرکت", model.CustomerFullName));

                col.Item().Element(c => PdfCommon.SectionBar(c, h, "مبلغ و ارز دریافتی", scale));
                FieldTable(col.Item(), ("مبلغ دریافتی", model.AmountText), ("نرخ تسعیر", model.ConversionRateText ?? "—"));

                col.Item().Element(c => PdfCommon.SectionBar(c, h, "گیرنده نهایی", scale));
                FieldTable(col.Item(), ("نام گیرنده نهایی", model.FinalRecipientName ?? "—"));

                col.Item().PaddingTop(10 * scale).ShowEntire().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(5727);
                        cd.RelativeColumn(3902);
                    });
                    foreach (var title in new[] { "دریافت‌کننده (شرکت)", "پرداخت‌کننده" })
                        t.Cell().Background(h.PrimaryColor).PaddingVertical(3).AlignCenter().Text(title).Bold().FontColor(Colors.White);
                    Signature(t.Cell(), $"نماینده {ShortName(h.PersianName)}");
                    Signature(t.Cell(), null);
                });
            });

            page.Footer().BorderTop(0.75f).BorderColor(h.PrimaryColor).PaddingTop(4).AlignCenter().Text(t =>
            {
                t.Span(h.FooterText).FontSize(8 * scale).FontColor(h.SubtleTextColor);
                t.Span("     |     ").FontSize(8 * scale).FontColor(h.SubtleTextColor);
                t.Span(BidiText.IsolateLatinRuns(h.EnglishName)).FontSize(7 * scale).FontColor(h.SubtleTextColor);
            });
        })).GeneratePdf();

        void Label(IContainer c, string text) =>
            PdfCommon.LabelCell(c, h).Text(text).Bold().FontSize(11 * scale).FontColor(h.PrimaryColor);

        void Value(IContainer c, string text, bool center = false)
        {
            var cell = PdfCommon.ValueCell(c, h);
            (center ? cell.AlignCenter() : cell).Text(BidiText.IsolateLatinRuns(text));
        }

        void FieldTable(IContainer c, params (string Label, string Value)[] rows) =>
            c.Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1900);
                    cd.RelativeColumn(7738);
                });
                foreach (var (label, value) in rows)
                {
                    Label(t.Cell(), label);
                    Value(t.Cell(), value);
                }
            });

        void Signature(IContainer c, string? subtitle) =>
            c.Border(0.5f).BorderColor(h.BorderColor).MinHeight(36).Padding(6).Column(s =>
            {
                s.Item().Text("نام و نام خانوادگی:").FontSize(11 * scale);
                if (subtitle is not null) s.Item().Text(subtitle).FontSize(10 * scale).FontColor(h.MutedTextColor);
                s.Item().PaddingTop(4).Text("تاریخ:").FontSize(11 * scale);
                s.Item().PaddingTop(4).PaddingBottom(45 * scale).AlignCenter().Text("امضا و مهر:").FontSize(11 * scale);
            });
    }

    /// <summary>"شرکت بازرگانی لنج – برادران …" → "شرکت بازرگانی لنج", as used in the template's signature block.</summary>
    private static string ShortName(string persianName)
    {
        var cut = persianName.IndexOfAny(new[] { '–', '-' });
        return (cut > 0 ? persianName[..cut] : persianName).Trim();
    }
}
