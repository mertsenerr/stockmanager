using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Rapor;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/raporlar")]
public sealed class RaporlarController : ControllerBase
{
    private readonly IOturumRepository _oturumlar;
    private readonly IMagazaRepository _magazalar;
    private readonly IFirmaRepository _firmalar;
    private readonly IUserRepository _users;

    public RaporlarController(
        IOturumRepository oturumlar,
        IMagazaRepository magazalar,
        IFirmaRepository firmalar,
        IUserRepository users)
    {
        _oturumlar = oturumlar;
        _magazalar = magazalar;
        _firmalar = firmalar;
        _users = users;
    }

    [HttpGet("magaza-sapma")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    public async Task<IActionResult> MagazaSapma(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var fromUtc = TryParseDate(from);
        var toUtc = TryParseDate(to);
        // C-1: SayimBaskani only sees stats for oturums they're a participant of, manage
        // a magaza for, or are invited to via email. Sistem keeps the cross-firma view.
        var oturumlar = await ListVisibleOturumlarAsync(fromUtc, toUtc, ct);

        var magazaIds = oturumlar.Select(o => o.MagazaId).Distinct();
        var magazaMap = (await _magazalar.ListByIdsAsync(magazaIds, ct))
            .ToDictionary(m => m.Id);
        var firmaCache = new Dictionary<string, string>();

        var grouped = oturumlar
            .GroupBy(o => o.MagazaId)
            .Select(g =>
            {
                magazaMap.TryGetValue(g.Key, out var m);
                var firmaId = m?.FirmaId ?? string.Empty;
                if (!firmaCache.TryGetValue(firmaId, out var firmaAdi))
                {
                    var f = string.IsNullOrEmpty(firmaId)
                        ? null
                        : _firmalar.FindByIdAsync(firmaId, ct).GetAwaiter().GetResult();
                    firmaAdi = f?.Ad ?? "?";
                    firmaCache[firmaId] = firmaAdi;
                }
                var toplam = g.Sum(o => o.Ozetler.ToplamUrun);
                var farkli = g.Sum(o => o.Ozetler.ToplamUrun - o.Ozetler.Onaylanmis);
                var pozitif = g.Sum(o => o.Ozetler.ToplamFarkPozitif);
                var negatif = g.Sum(o => o.Ozetler.ToplamFarkNegatif);
                return new MagazaSapmaDto
                {
                    MagazaId = g.Key,
                    MagazaAdi = m?.Ad ?? "?",
                    FirmaAdi = firmaAdi,
                    OturumSayisi = g.Count(),
                    ToplamUrun = toplam,
                    ToplamFarkliUrun = farkli,
                    ToplamFarkPozitif = pozitif,
                    ToplamFarkNegatif = negatif,
                    SapmaYuzdesi = toplam == 0 ? 0 : Math.Round((decimal)farkli / toplam * 100m, 2),
                };
            })
            .OrderByDescending(d => d.SapmaYuzdesi)
            .ToList();

        return Ok(grouped);
    }

    [HttpGet("sayman-performans")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    public async Task<IActionResult> SaymanPerformans(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var fromUtc = TryParseDate(from);
        var toUtc = TryParseDate(to);
        // C-1: scoped to caller-visible oturums (see MagazaSapma).
        var oturumlar = await ListVisibleOturumlarAsync(fromUtc, toUtc, ct);

        // We need full docs (Urunler) for change history aggregation; refetch detail per oturum.
        var perUser = new Dictionary<string, SaymanPerformansDto>();
        foreach (var summary in oturumlar)
        {
            var full = await _oturumlar.FindByIdAsync(summary.Id, ct);
            if (full is null) continue;
            foreach (var katilimci in full.Katilimcilar)
            {
                if (!perUser.TryGetValue(katilimci.KullaniciId, out var dto))
                {
                    dto = new SaymanPerformansDto
                    {
                        KullaniciId = katilimci.KullaniciId,
                    };
                    perUser[katilimci.KullaniciId] = dto;
                }
                dto.OturumSayisi++;
            }
            foreach (var urun in full.Urunler)
            {
                foreach (var ch in urun.DegisiklikGecmisi)
                {
                    if (!perUser.TryGetValue(ch.KullaniciId, out var dto))
                    {
                        dto = new SaymanPerformansDto { KullaniciId = ch.KullaniciId };
                        perUser[ch.KullaniciId] = dto;
                    }
                    dto.ToplamGuncelleme++;
                    if (dto.SonAktivite is null || ch.Tarih > dto.SonAktivite) dto.SonAktivite = ch.Tarih;
                }
                foreach (var y in urun.Yorumlar)
                {
                    if (!perUser.TryGetValue(y.KullaniciId, out var dto))
                    {
                        dto = new SaymanPerformansDto { KullaniciId = y.KullaniciId };
                        perUser[y.KullaniciId] = dto;
                    }
                    dto.ToplamYorum++;
                    if (dto.SonAktivite is null || y.Tarih > dto.SonAktivite) dto.SonAktivite = y.Tarih;
                }
            }
        }

        var users = (await _users.ListByIdsAsync(perUser.Keys, ct))
            .ToDictionary(u => u.Id, u => u.AdSoyad);
        foreach (var (id, dto) in perUser) dto.AdSoyad = users.GetValueOrDefault(id, "?");

        return Ok(perUser.Values.OrderByDescending(d => d.ToplamGuncelleme).ToList());
    }

    [HttpGet("oturum/{id}/excel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SayimYoneticisi}")]
    public async Task<IActionResult> ExportOturumExcel(string id, CancellationToken ct)
    {
        var oturum = await _oturumlar.FindByIdAsync(id, ct);
        if (oturum is null) return NotFound();
        // C-1: same access rule as the report list — Sistem unrestricted, others must be
        // a participant or own the magaza.
        if (!await IsCallerAllowedToReadOturumAsync(oturum, ct)) return Forbid();

        var magaza = await _magazalar.FindByIdAsync(oturum.MagazaId, ct);
        var firma = await _firmalar.FindByIdAsync(oturum.FirmaId, ct);

        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("Sayım");

        sheet.Cell(1, 1).Value = "Firma";
        sheet.Cell(1, 2).Value = firma?.Ad ?? "?";
        sheet.Cell(2, 1).Value = "Mağaza";
        sheet.Cell(2, 2).Value = magaza?.Ad ?? "?";
        sheet.Cell(3, 1).Value = "Tarih";
        sheet.Cell(3, 2).Value = oturum.Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        sheet.Cell(4, 1).Value = "Durum";
        sheet.Cell(4, 2).Value = oturum.Durum;
        sheet.Range(1, 1, 4, 1).Style.Font.Bold = true;

        var headerRow = 6;
        sheet.Cell(headerRow, 1).Value = "Barkod";
        sheet.Cell(headerRow, 2).Value = "Ürün";
        sheet.Cell(headerRow, 3).Value = "Sistem";
        sheet.Cell(headerRow, 4).Value = "Sayılan";
        sheet.Cell(headerRow, 5).Value = "Fark";
        sheet.Cell(headerRow, 6).Value = "Durum";
        sheet.Cell(headerRow, 7).Value = "Yorum sayısı";
        sheet.Range(headerRow, 1, headerRow, 7).Style.Font.Bold = true;
        sheet.Range(headerRow, 1, headerRow, 7).Style.Fill.BackgroundColor = XLColor.FromArgb(0x16, 0x16, 0x16);
        sheet.Range(headerRow, 1, headerRow, 7).Style.Font.FontColor = XLColor.FromArgb(0xFA, 0xFA, 0xFA);

        var row = headerRow + 1;
        foreach (var u in oturum.Urunler)
        {
            sheet.Cell(row, 1).Value = u.Barkod;
            sheet.Cell(row, 2).Value = u.UrunAdi;
            sheet.Cell(row, 3).Value = u.SistemStok;
            sheet.Cell(row, 4).Value = u.SayilanStok;
            sheet.Cell(row, 5).Value = u.SayilanStok - u.SistemStok;
            sheet.Cell(row, 6).Value = u.Durum;
            sheet.Cell(row, 7).Value = u.Yorumlar.Count;
            row++;
        }
        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        var fileName = $"sayim-{oturum.Tarih:yyyy-MM-dd}-{magaza?.Ad ?? oturum.MagazaId}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static DateTime? TryParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
    }

    // C-1 scoping helper: returns oturums visible to the caller in the given date range.
    // Sistem sees everything; SayimBaskani sees the union of (Katilimci + MagazaIds + DavetliMailler).
    // Mirrors OturumlarController.List's per-user scoping logic.
    private async Task<IReadOnlyList<SayimOturumu>> ListVisibleOturumlarAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        if (User.IsSistem())
            return await _oturumlar.ListAsync(null, null, fromUtc, toUtc, ct);

        var uid = User.GetUserId();
        if (uid is null) return Array.Empty<SayimOturumu>();

        var dbUser = await _users.FindByIdAsync(uid, ct);
        var myEmail = dbUser?.Email?.ToLowerInvariant();
        var myMagazaIds = dbUser?.MagazaIds ?? new List<string>();

        var participating = (await _oturumlar.ListWhereUserParticipatesAsync(uid, ct)).ToList();

        if (myMagazaIds.Count > 0)
        {
            var byMagaza = await _oturumlar.ListByMagazaIdsAsync(myMagazaIds, ct);
            participating = participating.Concat(byMagaza)
                .GroupBy(o => o.Id).Select(g => g.First()).ToList();
        }

        if (!string.IsNullOrEmpty(myEmail))
        {
            var all = await _oturumlar.ListAsync(null, null, fromUtc, toUtc, ct);
            var davetli = all.Where(o => o.DavetliMailler.Any(m =>
                string.Equals(m, myEmail, StringComparison.OrdinalIgnoreCase)));
            participating = participating.Concat(davetli)
                .GroupBy(o => o.Id).Select(g => g.First()).ToList();
        }

        return participating
            .Where(o => !fromUtc.HasValue || o.Tarih >= fromUtc.Value)
            .Where(o => !toUtc.HasValue || o.Tarih < toUtc.Value)
            .ToList();
    }

    private async Task<bool> IsCallerAllowedToReadOturumAsync(SayimOturumu oturum, CancellationToken ct)
    {
        if (User.IsSistem()) return true;
        var uid = User.GetUserId();
        if (uid is null) return false;
        if (oturum.Katilimcilar.Any(k => k.KullaniciId == uid)) return true;
        var dbUser = await _users.FindByIdAsync(uid, ct);
        if (dbUser is null) return false;
        if (dbUser.MagazaIds.Contains(oturum.MagazaId)) return true;
        var myEmail = dbUser.Email?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(myEmail) && oturum.DavetliMailler.Any(m =>
                string.Equals(m, myEmail, StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }
}
