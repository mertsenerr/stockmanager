using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

public interface IPdfSigner
{
    /// <summary>
    /// Mevcut bir PDF'in son sayfasının altına imza PNG'leri + dinamik kaşe
    /// damgası bindirir. Sonuç MemoryStream olarak döner.
    /// </summary>
    /// <exception cref="InvalidOperationException">PDF okunamaz veya şifrelidir.</exception>
    MemoryStream Stamp(Stream originalPdf, IReadOnlyList<DosyaImza> imzalar, KaseDamga? kase);
}

public sealed class PdfSigner : IPdfSigner
{
    private const double BlockHeight = 130;
    private const double BlockWidth = 180;
    private const double Padding = 16;
    private const double SignatureImageHeight = 50;
    private const double KaseDiameter = 110;

    public MemoryStream Stamp(Stream originalPdf, IReadOnlyList<DosyaImza> imzalar, KaseDamga? kase)
    {
        // PdfSharpCore.Open: bazı PDF'ler "encrypted" hatası verebilir; bu
        // durumda InvalidOperationException atıp controller'a 422 döndürmesini
        // söylüyoruz.
        PdfDocument doc;
        try
        {
            doc = PdfReader.Open(originalPdf, PdfDocumentOpenMode.Modify);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException("PDF açılamadı (şifreli veya bozuk olabilir).", ex);
        }

        var lastPage = doc.Pages[^1];
        using (var gfx = XGraphics.FromPdfPage(lastPage))
        {
            // Blokları sayfanın altına yatay olarak yerleştir.
            var blocks = new List<Action<double>>();
            foreach (var imza in imzalar)
            {
                blocks.Add(x => DrawSignatureBlock(gfx, x, lastPage.Height - BlockHeight - Padding, imza));
            }
            if (kase is not null)
            {
                blocks.Add(x => DrawKase(gfx, x, lastPage.Height - BlockHeight - Padding, kase));
            }

            if (blocks.Count == 0) return WriteToStream(doc);

            // Sayfaya sığacak şekilde ortala.
            var totalWidth = blocks.Count * BlockWidth + (blocks.Count - 1) * Padding;
            var startX = Math.Max(Padding, (lastPage.Width - totalWidth) / 2);
            for (var i = 0; i < blocks.Count; i++)
            {
                blocks[i](startX + i * (BlockWidth + Padding));
            }
        }

        return WriteToStream(doc);
    }

    private static MemoryStream WriteToStream(PdfDocument doc)
    {
        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    private static void DrawSignatureBlock(XGraphics gfx, double x, double y, DosyaImza imza)
    {
        var titleFont = new XFont("Arial", 8, XFontStyle.Bold);
        var nameFont = new XFont("Arial", 9, XFontStyle.Regular);
        var dateFont = new XFont("Arial", 7, XFontStyle.Regular);

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

        var bigFont = new XFont("Arial", 9, XFontStyle.Bold);
        var smallFont = new XFont("Arial", 7, XFontStyle.Regular);

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
