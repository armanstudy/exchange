using LenjExchange.Domain.Text;
using LenjExchange.Application.Settings;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

/// <summary>Letterhead and table styling taken from References/Receipt-Template.docx (see References/BRAND-AND-LAYOUT-SPEC.md).</summary>
internal static class PdfCommon
{
    public const string SubtleBorder = "#D9DFE6";
    private static readonly Lazy<byte[]> EmbeddedLogo = new(() =>
    {
        using var s = typeof(PdfCommon).Assembly.GetManifestResourceStream("LenjExchange.Logo.lenj-logo.png")
                      ?? throw new InvalidOperationException("Embedded logo missing.");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    public static byte[] LogoBytes(CompanyHeader h) =>
        !string.IsNullOrWhiteSpace(h.LogoPath) && File.Exists(h.LogoPath) ? File.ReadAllBytes(h.LogoPath!) : EmbeddedLogo.Value;

    /// <param name="scale">1 for A4; smaller for A5 so the letterhead keeps its proportions.</param>
    public static void CompanyHeader(IContainer container, CompanyHeader h, string title, string? rightInfo, string? leftInfo, float scale = 1f)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Width(51 * scale).Height(45 * scale).Image(LogoBytes(h)).FitArea();
            col.Item().PaddingTop(2).AlignCenter().Text(h.EnglishName).Bold().FontSize(11 * scale).FontColor(h.PrimaryColor);
            col.Item().AlignCenter().Text(h.PersianName).Bold().FontSize(10.5f * scale).FontColor(h.SecondaryColor);

            col.Item().PaddingTop(3).BorderBottom(1).BorderColor(h.PrimaryColor).PaddingBottom(3).Row(row =>
            {
                ContactColumn(row.RelativeItem(), h, h.IranLabel, h.IranAddress, h.IranContact, scale);
                row.ConstantItem(0.5f).Background(SubtleBorder);
                ContactColumn(row.RelativeItem(), h, h.ChinaLabel, h.ChinaAddress, h.ChinaContact, scale);
            });

            col.Item().PaddingTop(4).Background(h.PrimaryColor).PaddingVertical(3 * scale).AlignCenter()
                .Text(title).Bold().FontSize(15.5f * scale).FontColor(Colors.White);

            if (rightInfo is not null || leftInfo is not null)
                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().AlignRight().Text(BidiText.IsolateLatinRuns(rightInfo)).FontSize(9 * scale).FontColor(h.MutedTextColor);
                    row.RelativeItem().AlignLeft().Text(BidiText.IsolateLatinRuns(leftInfo)).FontSize(9 * scale).FontColor(h.MutedTextColor);
                });
        });
    }

    private static void ContactColumn(IContainer c, CompanyHeader h, string label, string address, string contact, float scale) =>
        c.PaddingHorizontal(5).Column(col =>
        {
            col.Item().AlignCenter().Text(t =>
            {
                t.Span(label).Bold().FontSize(10 * scale).FontColor(h.PrimaryColor);
                t.Span("  |  ").FontSize(9 * scale).FontColor(h.MutedTextColor);
                t.Span(BidiText.IsolateLatinRuns(address)).FontSize(9 * scale).FontColor(h.MutedTextColor);
            });
            col.Item().AlignCenter().Text(BidiText.IsolateLatinRuns(contact)).FontSize(8.5f * scale).FontColor(h.MutedTextColor);
        });

    public static void SectionBar(IContainer c, CompanyHeader h, string text, float scale = 1f) =>
        c.PaddingTop(5.5f * scale).PaddingBottom(3.75f * scale).Background(h.PrimaryColor).PaddingVertical(2).PaddingHorizontal(4.5f)
            .Text(text).Bold().FontSize(11.5f * scale).FontColor(Colors.White);

    public static IContainer LabelCell(IContainer c, CompanyHeader h) =>
        c.Border(0.5f).BorderColor(h.BorderColor).Background(h.LightColor).PaddingVertical(2.75f).PaddingHorizontal(4.5f).AlignMiddle();

    public static IContainer ValueCell(IContainer c, CompanyHeader h) =>
        c.Border(0.5f).BorderColor(h.BorderColor).Background(h.BackgroundColor).PaddingVertical(2.75f).PaddingHorizontal(4.5f).AlignMiddle();

    public static IContainer HeaderCell(IContainer c, CompanyHeader h) =>
        c.Background(h.PrimaryColor).PaddingVertical(4).PaddingHorizontal(4).DefaultTextStyle(x => x.FontColor(Colors.White).Bold().FontSize(9));

    public static IContainer BodyCell(IContainer c, CompanyHeader h, bool alt) =>
        c.Background(alt ? h.BackgroundColor : Colors.White).BorderBottom(0.5f).BorderColor(h.BorderColor)
            .PaddingVertical(3).PaddingHorizontal(4).DefaultTextStyle(x => x.FontSize(8.5f).FontColor(h.TextColor));

    public static void PageNumbers(IContainer c, CompanyHeader h) =>
        c.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8).FontColor(h.SubtleTextColor));
            t.Span("صفحه ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
}
