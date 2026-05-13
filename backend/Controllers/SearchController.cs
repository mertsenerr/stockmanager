using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Search;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Controllers;

/// <summary>
/// Global komut paleti (Cmd+K) için backing endpoint.
/// Firma / Mağaza / Oturum / Kullanıcı içinde tek bir sorguyla arar.
/// Sonuçlar her kategori için ayrı listelenir; her item ucu rota ipucu taşır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly IFirmaRepository _firmalar;
    private readonly IMagazaRepository _magazalar;
    private readonly IOturumRepository _oturumlar;
    private readonly IUserRepository _users;

    public SearchController(
        IFirmaRepository firmalar,
        IMagazaRepository magazalar,
        IOturumRepository oturumlar,
        IUserRepository users)
    {
        _firmalar = firmalar;
        _magazalar = magazalar;
        _oturumlar = oturumlar;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 6,
        CancellationToken ct = default)
    {
        var results = new SearchResultsDto();
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2) return Ok(results);

        limit = Math.Clamp(limit, 1, 20);

        var uid = User.GetUserId();
        var isSistem = User.IsSistem();

        // ─── Firmalar ────────────────────────────────────────────────────────
        var firmalar = (IEnumerable<Firma>)await _firmalar.ListAsync(includeInactive: false, ct);
        if (!isSistem && !string.IsNullOrEmpty(uid))
            firmalar = firmalar.Where(f => f.OlusturanKullaniciId == uid);

        results.Firmalar = firmalar
            .Where(f =>
                f.Ad.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(f.Kisaltma) && f.Kisaltma.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .Select(f => new SearchResultItem
            {
                Id = f.Id,
                Label = f.Ad,
                Subtitle = string.IsNullOrWhiteSpace(f.Kisaltma) ? null : f.Kisaltma,
                Badge = "Firma",
                Route = "/firmalar",
            })
            .ToList();

        // ─── Mağazalar ────────────────────────────────────────────────────────
        var ownedFirmaIds = isSistem
            ? null
            : (await _firmalar.ListAsync(includeInactive: false, ct))
                .Where(f => f.OlusturanKullaniciId == uid)
                .Select(f => f.Id)
                .ToHashSet();

        var magazalar = (IEnumerable<Magaza>)await _magazalar.ListAsync(firmaId: null, includeInactive: false, ct);
        if (ownedFirmaIds is not null)
            magazalar = magazalar.Where(m => ownedFirmaIds.Contains(m.FirmaId) || m.MuduruKullaniciId == uid);

        results.Magazalar = magazalar
            .Where(m =>
                m.Ad.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Sehir.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Ilce.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(m.Adres) && m.Adres.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .Select(m => new SearchResultItem
            {
                Id = m.Id,
                Label = m.Ad,
                Subtitle = $"{m.Sehir} · {m.Ilce}",
                Badge = "Mağaza",
                Route = "/magazalar",
            })
            .ToList();

        // ─── Oturumlar ────────────────────────────────────────────────────────
        // Oturum modelinde direkt text alan yok; magaza/firma adıyla eşleşenleri
        // çıkış noktası olarak kullanıyoruz: sorguyla eşleşen firma/mağaza varsa
        // o firmaya/mağazaya bağlı son aktif oturumları sonuç olarak göster.
        var matchedFirmaIds = results.Firmalar.Select(f => f.Id).ToHashSet();
        var matchedMagazaIds = results.Magazalar.Select(m => m.Id).ToHashSet();
        if (matchedFirmaIds.Count > 0 || matchedMagazaIds.Count > 0)
        {
            var allOturumlar = isSistem
                ? await _oturumlar.ListAsync(null, null, null, null, ct)
                : (string.IsNullOrEmpty(uid)
                    ? Array.Empty<SayimOturumu>()
                    : (IReadOnlyList<SayimOturumu>)(await _oturumlar.ListWhereUserParticipatesAsync(uid, ct)));

            var firmaAdById = (await _firmalar.ListAsync(includeInactive: true, ct))
                .ToDictionary(f => f.Id, f => f.Ad);
            var magazaAdById = (await _magazalar.ListAsync(null, includeInactive: true, ct))
                .ToDictionary(m => m.Id, m => m.Ad);

            results.Oturumlar = allOturumlar
                .Where(o => matchedFirmaIds.Contains(o.FirmaId) || matchedMagazaIds.Contains(o.MagazaId))
                .OrderByDescending(o => o.Tarih)
                .Take(limit)
                .Select(o => new SearchResultItem
                {
                    Id = o.Id,
                    Label = magazaAdById.TryGetValue(o.MagazaId, out var ma) ? ma : "Mağaza",
                    Subtitle = (firmaAdById.TryGetValue(o.FirmaId, out var fa) ? fa : "")
                               + " · " + o.Tarih.ToString("dd.MM.yyyy"),
                    Badge = o.Durum,
                    Route = $"/oturumlar/{o.Id}",
                })
                .ToList();
        }

        // ─── Kullanıcılar ────────────────────────────────────────────────────
        // Sadece Sistem rolündeki kullanıcı global user search yapabilir;
        // diğer roller kendi ekibi / arkadaş listesi dışında kullanıcı görmemeli.
        if (isSistem)
        {
            var allUsers = await _users.ListAsync(includeInactive: false, ct);
            results.Kullanicilar = allUsers
                .Where(u =>
                    u.AdSoyad.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select(u => new SearchResultItem
                {
                    Id = u.Id,
                    Label = u.AdSoyad,
                    Subtitle = u.Email,
                    Badge = u.Rol,
                    Route = "/kullanicilar",
                })
                .ToList();
        }

        return Ok(results);
    }
}
