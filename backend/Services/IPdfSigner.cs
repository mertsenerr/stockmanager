using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

/// <summary>
/// PdfSharpCore'un IFontResolver implementasyonu — assembly'ye embed edilmiş
/// Noto Sans dosyalarını döner. Linux container'larda system font'a bağımlı
/// kalmamak için. Process başına bir defa GlobalFontSettings.FontResolver'a
/// atanmalı.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    private const string DefaultFamily = "Noto Sans";
    private const string RegularResource = "SayimLink.Api.Resources.Fonts.NotoSans-Regular.ttf";
    private const string BoldResource = "SayimLink.Api.Resources.Fonts.NotoSans-Bold.ttf";

    public string DefaultFontName => DefaultFamily;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Hangi aile istenirse istensin (Arial, Helvetica, vs.) Noto'ya yönlendir
        // — Linux container'larda eldeki tek font bu, ve metric'leri benzer.
        var face = isBold ? "NotoSans#bold" : "NotoSans#regular";
        return new FontResolverInfo(face);
    }

    public byte[] GetFont(string faceName)
    {
        var resourceName = faceName.EndsWith("bold", StringComparison.OrdinalIgnoreCase)
            ? BoldResource
            : RegularResource;
        using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font not found: {resourceName}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Process başına bir kez global resolver olarak atar.</summary>
    public static void Install()
    {
        if (GlobalFontSettings.FontResolver is EmbeddedFontResolver) return;
        GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    }
}

public interface IPdfSigner
{
    /// <summary>
    /// Mevcut bir PDF'in son sayfasının altına imza PNG'leri + dinamik kaşe
    /// damgası bindirir. Yerleşim, belge tipinin tanımladığı konum
    /// (SolAlt/OrtaAlt/SagAlt) snapshot'larına göre yapılır.
    /// </summary>
    /// <exception cref="InvalidOperationException">PDF okunamaz veya şifrelidir.</exception>
    MemoryStream Stamp(
        Stream originalPdf,
        IReadOnlyList<DosyaImza> imzalar,
        IReadOnlyList<ImzaSlot> slotlar,
        KaseDamga? kase,
        string? kaseKonum);
}

public sealed class PdfSigner : IPdfSigner
{
    private const double BlockHeight = 130;
    private const double BlockWidth = 180;
    private const double Padding = 16;
    private const double SignatureImageHeight = 50;
    private const double KaseDiameter = 110;
    private const string FontFamily = "Noto Sans";

    public PdfSigner()
    {
        // FontResolver process-wide global — DI singleton constructor'da
        // bir kez kurulur, sonraki çağrılar idempotent.
        EmbeddedFontResolver.Install();
    }

    public MemoryStream Stamp(
        Stream originalPdf,
        IReadOnlyList<DosyaImza> imzalar,
        IReadOnlyList<ImzaSlot> slotlar,
        KaseDamga? kase,
        string? kaseKonum)
    {
        // GridFS download stream'i seek desteklemiyor, PdfReader seek istiyor —
        // MemoryStream'e buffer'la, sonra ondan oku.
        using var buffered = new MemoryStream();
        originalPdf.CopyTo(buffered);
        buffered.Position = 0;

        // PdfSharpCore.Open: bazı PDF'ler "encrypted" hatası verebilir; bu
        // durumda InvalidOperationException atıp controller'a 422 döndürmesini
        // söylüyoruz.
        PdfDocument doc;
        try
        {
            doc = PdfReader.Open(buffered, PdfDocumentOpenMode.Modify);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException("PDF açılamadı (şifreli veya bozuk olabilir).", ex);
        }

        var lastPage = doc.Pages[^1];
        using (var gfx = XGraphics.FromPdfPage(lastPage))
        {
            var y = lastPage.Height - BlockHeight - Padding;

            // Her imza, slot tanımındaki konuma yerleştirilir. Slot bulunamazsa
            // (eski snapshot) OrtaAlt fallback.
            foreach (var imza in imzalar)
            {
                var slot = slotlar.FirstOrDefault(s => s.Rol == imza.Rol);
                var konum = slot?.Konum ?? ImzaKonumlari.OrtaAlt;
                var x = KonumToX(konum, lastPage.Width);
                DrawSignatureBlock(gfx, x, y, imza);
            }

            if (kase is not null)
            {
                var x = KonumToX(kaseKonum ?? ImzaKonumlari.OrtaAlt, lastPage.Width);
                DrawKase(gfx, x, y, kase);
            }
        }

        return WriteToStream(doc);
    }

    private static double KonumToX(string konum, double pageWidth) => konum switch
    {
        ImzaKonumlari.SolAlt => Padding,
        ImzaKonumlari.SagAlt => pageWidth - BlockWidth - Padding,
        _ => (pageWidth - BlockWidth) / 2, // OrtaAlt default
    };

    private static MemoryStream WriteToStream(PdfDocument doc)
    {
        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    private static void DrawSignatureBlock(XGraphics gfx, double x, double y, DosyaImza imza)
    {
        var titleFont = new XFont(FontFamily, 8, XFontStyle.Bold);
        var nameFont = new XFont(FontFamily, 9, XFontStyle.Regular);
        var dateFont = new XFont(FontFamily, 7, XFontStyle.Regular);

        var ink = XBrushes.Black;
        var muted = new XSolidBrush(XColor.FromArgb(110, 110, 110));

        // Üst başlık: rol
        var rolLabel = ImzaRolEtiketi(imza.Rol);
        gfx.DrawString(rolLabel, titleFont, ink, new XRect(x, y, BlockWidth, 12), XStringFormats.TopLeft);

        // İmza görseli (PNG)
        var imageY = y + 14;
        var bytes = DecodeDataUri(imza.ImzaGorseliDataUri);
        if (bytes is not null)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var img = XImage.FromStream(() => stream);
                var aspect = (double)img.PixelWidth / Math.Max(1, img.PixelHeight);
                var drawHeight = SignatureImageHeight;
                var drawWidth = Math.Min(BlockWidth, drawHeight * aspect);
                gfx.DrawImage(img, x, imageY, drawWidth, drawHeight);
            }
            catch
            {
                gfx.DrawString("(imza görseli render edilemedi)", dateFont, muted,
                    new XRect(x, imageY, BlockWidth, SignatureImageHeight), XStringFormats.TopLeft);
            }
        }

        // İmza altına çizgi
        var lineY = imageY + SignatureImageHeight + 4;
        gfx.DrawLine(XPens.Black, x, lineY, x + BlockWidth, lineY);

        // Ad soyad + tarih
        gfx.DrawString(imza.KullaniciAdSoyad, nameFont, ink,
            new XRect(x, lineY + 4, BlockWidth, 14), XStringFormats.TopLeft);
        gfx.DrawString(imza.ImzalanmaTarihi.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), dateFont, muted,
            new XRect(x, lineY + 18, BlockWidth, 12), XStringFormats.TopLeft);
    }

    private static void DrawKase(XGraphics gfx, double x, double y, KaseDamga kase)
    {
        // Dinamik mağaza kaşesi: kırmızı tonlu çift halka, iç içe iki yay text.
        var diameter = KaseDiameter;
        var centerX = x + BlockWidth / 2;
        var centerY = y + BlockHeight / 2 - 5;
        var rect = new XRect(centerX - diameter / 2, centerY - diameter / 2, diameter, diameter);

        var redPen = new XPen(XColor.FromArgb(170, 30, 30), 2);
        var redThinPen = new XPen(XColor.FromArgb(170, 30, 30), 1);
        var redBrush = new XSolidBrush(XColor.FromArgb(170, 30, 30));

        // Dış halka
        gfx.DrawEllipse(redPen, rect);
        // İç halka
        var innerRect = new XRect(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12);
        gfx.DrawEllipse(redThinPen, innerRect);

        // Orta yatay çizgi (klasik "kabul edildi" damgası gibi)
        gfx.DrawLine(redThinPen, centerX - diameter / 2 + 6, centerY, centerX + diameter / 2 - 6, centerY);

        var bigFont = new XFont(FontFamily, 9, XFontStyle.Bold);
        var smallFont = new XFont(FontFamily, 7, XFontStyle.Regular);

        // Üst yarı: "MAĞAZA KAŞESİ"
        gfx.DrawString("MAĞAZA KAŞESİ", bigFont, redBrush,
            new XRect(centerX - diameter / 2, centerY - 22, diameter, 12), XStringFormats.Center);
        // Orta: kişi adı (kısaltma — sığmayabilir)
        var ad = kase.BasanAdSoyad;
        if (ad.Length > 22) ad = ad[..22] + "…";
        gfx.DrawString(ad, smallFont, redBrush,
            new XRect(centerX - diameter / 2, centerY + 4, diameter, 10), XStringFormats.Center);
        // Alt yarı: tarih
        gfx.DrawString(kase.Tarih.ToLocalTime().ToString("dd.MM.yyyy"), smallFont, redBrush,
            new XRect(centerX - diameter / 2, centerY + 16, diameter, 10), XStringFormats.Center);
    }

    private static string ImzaRolEtiketi(string rol) => rol switch
    {
        ImzaRolleri.SayimBaskani => "SAYIM BAŞKANI",
        ImzaRolleri.MagazaYetkilisi => "MAĞAZA YETKİLİSİ",
        _ => rol.ToUpperInvariant(),
    };

    private static byte[]? DecodeDataUri(string dataUri)
    {
        if (string.IsNullOrEmpty(dataUri)) return null;
        var comma = dataUri.IndexOf(',');
        if (comma < 0) return null;
        try { return Convert.FromBase64String(dataUri[(comma + 1)..]); }
        catch { return null; }
    }
}
