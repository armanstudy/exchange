using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace LenjExchange.Infrastructure.Pdf;

/// <summary>Registers the embedded Vazirmatn font (SIL OFL 1.1) once and configures the QuestPDF Community license.</summary>
public static class PdfFonts
{
    public const string Family = "Vazirmatn";
    private static readonly object Sync = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Sync)
        {
            if (_initialized) return;
            // Community license: free for organisations under the QuestPDF revenue threshold — see docs/open-questions.md.
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseEnvironmentFonts = false;
            var asm = typeof(PdfFonts).Assembly;
            foreach (var name in new[] { "LenjExchange.Fonts.Vazirmatn-Regular.ttf", "LenjExchange.Fonts.Vazirmatn-Bold.ttf" })
            {
                using var s = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Embedded font {name} missing.");
                FontManager.RegisterFontWithCustomName(Family, s);
            }
            _initialized = true;
        }
    }
}
