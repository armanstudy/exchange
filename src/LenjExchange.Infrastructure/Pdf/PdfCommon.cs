using LenjExchange.Application.Settings;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

internal static class PdfCommon
{
    public static void CompanyHeader(IContainer container, CompanyHeader h, string title, string? rightInfo, string? leftInfo)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(70).Height(60).Element(logo =>
                {
                    if (!string.IsNullOrWhiteSpace(h.LogoPath) && File.Exists(h.LogoPath))
                        logo.Image(h.LogoPath!).FitArea();
                    else
                        logo.Border(1).BorderColor(h.LightColor).Background(h.BackgroundColor)
                            .AlignCenter().AlignMiddle().Text("لوگو").FontColor(h.MutedTextColor).FontSize(9);
                });
                row.RelativeItem().PaddingHorizontal(10).Column(c =>
                {
                    c.Item().Text(h.PersianName).Bold().FontSize(13).FontColor(h.PrimaryColor);
                    c.Item().Text(h.EnglishName).FontSize(9).FontColor(h.PrimaryColor);
                    c.Item().Text(h.ContactLine).FontSize(8).FontColor(h.MutedTextColor);
                });
            });
            col.Item().PaddingTop(6).Height(3).Background(h.PrimaryColor);
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().AlignLeft().Text(rightInfo ?? "").FontSize(9).FontColor(h.MutedTextColor);
                row.RelativeItem().AlignCenter().Text(title).Bold().FontSize(15).FontColor(h.PrimaryColor);
                row.RelativeItem().AlignRight().Text(leftInfo ?? "").FontSize(9).FontColor(h.MutedTextColor);
            });
        });
    }

    public static IContainer HeaderCell(IContainer c, CompanyHeader h) =>
        c.Background(h.PrimaryColor).PaddingVertical(4).PaddingHorizontal(4).DefaultTextStyle(x => x.FontColor(Colors.White).Bold().FontSize(9));

    public static IContainer BodyCell(IContainer c, CompanyHeader h, bool alt) =>
        c.Background(alt ? h.BackgroundColor : Colors.White).BorderBottom(0.5f).BorderColor(h.LightColor)
            .PaddingVertical(3).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(8.5f));

    public static void PageNumbers(IContainer c, CompanyHeader h) =>
        c.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor(h.MutedTextColor));
            t.Span("صفحه ");
            t.CurrentPageNumber();
            t.Span(" از ");
            t.TotalPages();
        });
}
