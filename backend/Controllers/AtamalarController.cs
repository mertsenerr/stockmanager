using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Dtos.Takvim;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/atamalar")]
public sealed class AtamalarController : ControllerBase
{
    private readonly IAtamaRepository _atamalar;
    private readonly IMagazaRepository _magazalar;
    private readonly IFirmaRepository _firmalar;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly IValidator<AtamaUpsertRequest> _upsertValidator;
    private readonly IValidator<AtamaTarihUpdateRequest> _tarihValidator;

    public AtamalarController(
        IAtamaRepository atamalar,
        IMagazaRepository magazalar,
        IFirmaRepository firmalar,
        IUserRepository users,
        IAuditService audit,
        IValidator<AtamaUpsertRequest> upsertValidator,
        IValidator<AtamaTarihUpdateRequest> tarihValidator)
    {
        _atamalar = atamalar;
        _magazalar = magazalar;
        _firmalar = firmalar;
        _users = users;
        _audit = audit;
        _upsertValidator = upsertValidator;
        _tarihValidator = tarihValidator;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        if (!TryParseDate(from, out var fromUtc) || !TryParseDate(to, out var toUtc))
            return BadRequest(new { message = "Geçersiz tarih aralığı." });
        if (toUtc <= fromUtc)
            return BadRequest(new { message = "Bitiş tarihi başlangıçtan büyük olmalı." });
        if ((toUtc - fromUtc).TotalDays > 400)
            return BadRequest(new { message = "Aralık en fazla 400 gün olabilir." });

        IReadOnlyList<Atama> atamalar;
        if (Roles.IsAdminLevel(User))
        {
            atamalar = await _atamalar.ListByDateRangeAsync(fromUtc, toUtc, ct);
        }
        else
        {
            var userId = CurrentUserId();
            if (userId is null) return Unauthorized();
            atamalar = await _atamalar.ListForUserAsync(userId, fromUtc, toUtc, ct);

            // Mağaza Müdürü additionally sees atamalar for their assigned magazas.
            if (User.IsInRole(Roles.MagazaMuduru))
            {
                var magazaIds = (User.FindFirst("magazaIds")?.Value ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();
                if (magazaIds.Count > 0)
                {
                    var allInRange = await _atamalar.ListByDateRangeAsync(fromUtc, toUtc, ct);
                    var ownedMagaza = allInRange.Where(a => magazaIds.Contains(a.MagazaId));
                    atamalar = atamalar.Concat(ownedMagaza)
                        .GroupBy(a => a.Id).Select(g => g.First()).ToList();
                }
            }
        }

        return Ok(await EnrichManyAsync(atamalar, ct));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var atama = await _atamalar.FindByIdAsync(id, ct);
        if (atama is null) return NotFound();
        if (!CanRead(atama)) return Forbid();
        return Ok(await EnrichOneAsync(atama, ct));
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Create([FromBody] AtamaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _upsertValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var magaza = await _magazalar.FindByIdAsync(request.MagazaId, ct);
        if (magaza is null) return BadRequest(new { message = "Mağaza bulunamadı." });

        var atama = new Atama
        {
            MagazaId = magaza.Id,
            FirmaId = magaza.FirmaId,
            Tarih = ParseUtcMidnight(request.Tarih),
            BaslangicSaati = NormalizeTime(request.BaslangicSaati),
            BitisSaati = NormalizeTime(request.BitisSaati),
            YoneticiKullaniciId = request.YoneticiKullaniciId,
            SaymanKullaniciIds = request.SaymanKullaniciIds
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList(),
            Notlar = string.IsNullOrWhiteSpace(request.Notlar) ? null : request.Notlar.Trim(),
            Durum = request.Durum,
            OlusturanKullaniciId = CurrentUserId(),
        };
        await _atamalar.InsertAsync(atama, ct);
        _audit.Log(User, AuditAksiyonlari.AtamaCreate, "atama", atama.Id,
            yeni: $"{magaza.Ad} · {atama.Tarih:yyyy-MM-dd}");
        return CreatedAtAction(nameof(Get), new { id = atama.Id }, await EnrichOneAsync(atama, ct));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Update(string id, [FromBody] AtamaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _upsertValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var atama = await _atamalar.FindByIdAsync(id, ct);
        if (atama is null) return NotFound();

        var magaza = await _magazalar.FindByIdAsync(request.MagazaId, ct);
        if (magaza is null) return BadRequest(new { message = "Mağaza bulunamadı." });

        atama.MagazaId = magaza.Id;
        atama.FirmaId = magaza.FirmaId;
        atama.Tarih = ParseUtcMidnight(request.Tarih);
        atama.BaslangicSaati = NormalizeTime(request.BaslangicSaati);
        atama.BitisSaati = NormalizeTime(request.BitisSaati);
        atama.YoneticiKullaniciId = request.YoneticiKullaniciId;
        atama.SaymanKullaniciIds = request.SaymanKullaniciIds
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        atama.Notlar = string.IsNullOrWhiteSpace(request.Notlar) ? null : request.Notlar.Trim();
        atama.Durum = request.Durum;

        await _atamalar.ReplaceAsync(atama, ct);
        _audit.Log(User, AuditAksiyonlari.AtamaUpdate, "atama", atama.Id);
        return Ok(await EnrichOneAsync(atama, ct));
    }

    [HttpPatch("{id}/tarih")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> MoveDate(string id, [FromBody] AtamaTarihUpdateRequest request, CancellationToken ct)
    {
        var validation = await _tarihValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var atama = await _atamalar.FindByIdAsync(id, ct);
        if (atama is null) return NotFound();

        var oldDate = atama.Tarih.ToString("yyyy-MM-dd");
        await _atamalar.UpdateDateAsync(id, ParseUtcMidnight(request.Tarih), ct);
        atama.Tarih = ParseUtcMidnight(request.Tarih);
        _audit.Log(User, AuditAksiyonlari.AtamaMoveDate, "atama", id, eski: oldDate, yeni: request.Tarih);
        return Ok(await EnrichOneAsync(atama, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var atama = await _atamalar.FindByIdAsync(id, ct);
        if (atama is null) return NotFound();
        await _atamalar.DeleteAsync(id, ct);
        _audit.Log(User, AuditAksiyonlari.AtamaDelete, "atama", id);
        return NoContent();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private string? CurrentUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private bool CanRead(Atama atama)
    {
        if (Roles.IsAdminLevel(User)) return true;
        var uid = CurrentUserId();
        if (uid is null) return false;
        if (atama.YoneticiKullaniciId == uid) return true;
        if (atama.SaymanKullaniciIds.Contains(uid)) return true;
        if (User.IsInRole(Roles.MagazaMuduru))
        {
            var magazaIds = (User.FindFirst("magazaIds")?.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (magazaIds.Contains(atama.MagazaId)) return true;
        }
        return false;
    }

    private async Task<AtamaDto> EnrichOneAsync(Atama atama, CancellationToken ct)
    {
        var magaza = await _magazalar.FindByIdAsync(atama.MagazaId, ct);
        var firma = await _firmalar.FindByIdAsync(atama.FirmaId, ct);
        var allUserIds = atama.SaymanKullaniciIds.ToList();
        allUserIds.Add(atama.YoneticiKullaniciId);
        var userMap = (await _users.ListByIdsAsync(allUserIds.Distinct(), ct))
            .ToDictionary(u => u.Id, u => u.AdSoyad);

        return new AtamaDto
        {
            Id = atama.Id,
            MagazaId = atama.MagazaId,
            MagazaAdi = magaza?.Ad ?? "?",
            FirmaId = atama.FirmaId,
            FirmaAdi = firma?.Ad ?? "?",
            Tarih = atama.Tarih,
            BaslangicSaati = atama.BaslangicSaati,
            BitisSaati = atama.BitisSaati,
            YoneticiKullaniciId = atama.YoneticiKullaniciId,
            YoneticiAdi = userMap.GetValueOrDefault(atama.YoneticiKullaniciId, "?"),
            SaymanKullaniciIds = atama.SaymanKullaniciIds,
            SaymanAdlari = atama.SaymanKullaniciIds.Select(id => userMap.GetValueOrDefault(id, "?")).ToList(),
            Notlar = atama.Notlar,
            Durum = atama.Durum,
        };
    }

    private async Task<IReadOnlyList<AtamaDto>> EnrichManyAsync(IReadOnlyList<Atama> atamalar, CancellationToken ct)
    {
        if (atamalar.Count == 0) return Array.Empty<AtamaDto>();

        var magazaIds = atamalar.Select(a => a.MagazaId).Distinct().ToList();
        var firmaIds = atamalar.Select(a => a.FirmaId).Distinct().ToList();
        var userIds = atamalar.SelectMany(a => a.SaymanKullaniciIds.Concat(new[] { a.YoneticiKullaniciId }))
            .Distinct().ToList();

        var magazas = (await _magazalar.ListByIdsAsync(magazaIds, ct)).ToDictionary(m => m.Id, m => m.Ad);
        var firmas = new Dictionary<string, string>();
        foreach (var id in firmaIds)
        {
            var f = await _firmalar.FindByIdAsync(id, ct);
            if (f is not null) firmas[id] = f.Ad;
        }
        var userMap = (await _users.ListByIdsAsync(userIds, ct)).ToDictionary(u => u.Id, u => u.AdSoyad);

        return atamalar.Select(a => new AtamaDto
        {
            Id = a.Id,
            MagazaId = a.MagazaId,
            MagazaAdi = magazas.GetValueOrDefault(a.MagazaId, "?"),
            FirmaId = a.FirmaId,
            FirmaAdi = firmas.GetValueOrDefault(a.FirmaId, "?"),
            Tarih = a.Tarih,
            BaslangicSaati = a.BaslangicSaati,
            BitisSaati = a.BitisSaati,
            YoneticiKullaniciId = a.YoneticiKullaniciId,
            YoneticiAdi = userMap.GetValueOrDefault(a.YoneticiKullaniciId, "?"),
            SaymanKullaniciIds = a.SaymanKullaniciIds,
            SaymanAdlari = a.SaymanKullaniciIds.Select(id => userMap.GetValueOrDefault(id, "?")).ToList(),
            Notlar = a.Notlar,
            Durum = a.Durum,
        }).ToList();
    }

    private static bool TryParseDate(string? s, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)) return false;
        utc = DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return true;
    }

    private static DateTime ParseUtcMidnight(string yyyyMMdd)
    {
        var d = DateTime.ParseExact(yyyyMMdd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    private static string? NormalizeTime(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        return TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t) ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
    }

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
