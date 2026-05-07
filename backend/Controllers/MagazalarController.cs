using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/magazalar")]
public sealed class MagazalarController : ControllerBase
{
    private readonly IMagazaRepository _magazalar;
    private readonly IFirmaRepository _firmalar;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly IValidator<MagazaUpsertRequest> _validator;

    public MagazalarController(
        IMagazaRepository magazalar,
        IFirmaRepository firmalar,
        IUserRepository users,
        IAuditService audit,
        IValidator<MagazaUpsertRequest> validator)
    {
        _magazalar = magazalar;
        _firmalar = firmalar;
        _users = users;
        _audit = audit;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? firmaId,
        [FromQuery] bool includeInactive,
        CancellationToken ct)
    {
        // C-1: Only Sistem sees all magazas. Everyone else (incl. SayimBaskani) is scoped to
        // magazas in their MagazaIds plus magazas they created (so a SayimBaskani who creates
        // a store for someone else's management doesn't lose visibility of it).
        var all = await _magazalar.ListAsync(firmaId, includeInactive, ct);
        if (!User.IsSistem())
        {
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var dbUser = uid is null ? null : await _users.FindByIdAsync(uid, ct);
            var allowedIds = (dbUser?.MagazaIds ?? new List<string>()).ToHashSet();
            all = all.Where(m => allowedIds.Contains(m.Id) || m.OlusturanKullaniciId == uid).ToList();
        }

        var firmaIds = all.Select(m => m.FirmaId).Distinct().ToList();
        var firmaList = (await Task.WhenAll(firmaIds.Select(id => _firmalar.FindByIdAsync(id, ct))))
            .Where(f => f is not null).ToDictionary(f => f!.Id, f => f!.Ad);

        var mudurIds = all.Where(m => m.MuduruKullaniciId is not null).Select(m => m.MuduruKullaniciId!).Distinct();
        var mudurMap = (await _users.ListByIdsAsync(mudurIds, ct)).ToDictionary(u => u.Id, u => u.AdSoyad);

        return Ok(all.Select(m => ToDto(m, firmaList, mudurMap)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var magaza = await _magazalar.FindByIdAsync(id, ct);
        if (magaza is null) return NotFound();

        // C-1: same scope as List — Sistem sees all; others must be in MagazaIds or be the creator.
        if (!User.IsSistem())
        {
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var dbUser = uid is null ? null : await _users.FindByIdAsync(uid, ct);
            var canSee = dbUser is not null
                && (dbUser.MagazaIds.Contains(id) || magaza.OlusturanKullaniciId == uid);
            if (!canSee) return Forbid();
        }

        var firma = await _firmalar.FindByIdAsync(magaza.FirmaId, ct);
        var mudur = magaza.MuduruKullaniciId is null
            ? null
            : await _users.FindByIdAsync(magaza.MuduruKullaniciId, ct);

        return Ok(ToDto(magaza,
            firma is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [firma.Id] = firma.Ad },
            mudur is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [mudur.Id] = mudur.AdSoyad }));
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Create([FromBody] MagazaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var firma = await _firmalar.FindByIdAsync(request.FirmaId, ct);
        if (firma is null) return BadRequest(new { message = "Firma bulunamadı." });

        var magaza = new Magaza
        {
            FirmaId = request.FirmaId,
            Ad = request.Ad.Trim(),
            Sehir = request.Sehir.Trim(),
            Ilce = request.Ilce.Trim(),
            Adres = request.Adres.Trim(),
            Koordinat = request.Koordinat is null
                ? null
                : new Koordinat { Lat = request.Koordinat.Lat, Lng = request.Koordinat.Lng },
            MuduruKullaniciId = request.MuduruKullaniciId,
            AktifMi = request.AktifMi,
            OlusturanKullaniciId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        };
        await _magazalar.InsertAsync(magaza, ct);

        if (!string.IsNullOrEmpty(magaza.MuduruKullaniciId))
            await _users.AddMagazaToUserAsync(magaza.MuduruKullaniciId, magaza.Id, ct);

        _audit.Log(User, AuditAksiyonlari.MagazaCreate, "magaza", magaza.Id, yeni: $"{firma.Ad} · {magaza.Ad}");
        return CreatedAtAction(nameof(Get), new { id = magaza.Id }, await GetEnrichedAsync(magaza, ct));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Update(string id, [FromBody] MagazaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var magaza = await _magazalar.FindByIdAsync(id, ct);
        if (magaza is null) return NotFound();
        // Phase 3: non-Sistem may only update magazas they created.
        if (!User.IsSistem() && magaza.OlusturanKullaniciId != User.GetUserId()) return Forbid();

        var oldMudurId = magaza.MuduruKullaniciId;

        magaza.FirmaId = request.FirmaId;
        magaza.Ad = request.Ad.Trim();
        magaza.Sehir = request.Sehir.Trim();
        magaza.Ilce = request.Ilce.Trim();
        magaza.Adres = request.Adres.Trim();
        magaza.Koordinat = request.Koordinat is null
            ? null
            : new Koordinat { Lat = request.Koordinat.Lat, Lng = request.Koordinat.Lng };
        magaza.MuduruKullaniciId = request.MuduruKullaniciId;
        magaza.AktifMi = request.AktifMi;
        await _magazalar.ReplaceAsync(magaza, ct);

        if (oldMudurId != magaza.MuduruKullaniciId)
        {
            if (!string.IsNullOrEmpty(oldMudurId))
                await _users.RemoveMagazaFromUserAsync(oldMudurId, magaza.Id, ct);
            if (!string.IsNullOrEmpty(magaza.MuduruKullaniciId))
                await _users.AddMagazaToUserAsync(magaza.MuduruKullaniciId, magaza.Id, ct);
        }

        _audit.Log(User, AuditAksiyonlari.MagazaUpdate, "magaza", magaza.Id, yeni: magaza.Ad);
        return Ok(await GetEnrichedAsync(magaza, ct));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> SoftDelete(string id, CancellationToken ct)
    {
        var magaza = await _magazalar.FindByIdAsync(id, ct);
        if (magaza is null) return NotFound();
        // Phase 3: non-Sistem may only delete magazas they created.
        if (!User.IsSistem() && magaza.OlusturanKullaniciId != User.GetUserId()) return Forbid();

        await _magazalar.SoftDeleteAsync(id, ct);
        if (!string.IsNullOrEmpty(magaza.MuduruKullaniciId))
            await _users.RemoveMagazaFromUserAsync(magaza.MuduruKullaniciId, id, ct);

        _audit.Log(User, AuditAksiyonlari.MagazaDelete, "magaza", id, eski: magaza.Ad);
        return NoContent();
    }

    private async Task<MagazaDto> GetEnrichedAsync(Magaza magaza, CancellationToken ct)
    {
        var firma = await _firmalar.FindByIdAsync(magaza.FirmaId, ct);
        var mudur = magaza.MuduruKullaniciId is null
            ? null
            : await _users.FindByIdAsync(magaza.MuduruKullaniciId, ct);
        return ToDto(magaza,
            firma is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [firma.Id] = firma.Ad },
            mudur is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [mudur.Id] = mudur.AdSoyad });
    }

    private static MagazaDto ToDto(
        Magaza m,
        IReadOnlyDictionary<string, string> firmaMap,
        IReadOnlyDictionary<string, string> mudurMap) => new()
    {
        Id = m.Id,
        FirmaId = m.FirmaId,
        FirmaAdi = firmaMap.TryGetValue(m.FirmaId, out var ad) ? ad : null,
        Ad = m.Ad,
        Sehir = m.Sehir,
        Ilce = m.Ilce,
        Adres = m.Adres,
        Koordinat = m.Koordinat is null ? null : new() { Lat = m.Koordinat.Lat, Lng = m.Koordinat.Lng },
        MuduruKullaniciId = m.MuduruKullaniciId,
        MuduruAdSoyad = m.MuduruKullaniciId is not null && mudurMap.TryGetValue(m.MuduruKullaniciId, out var name)
            ? name : null,
        AktifMi = m.AktifMi,
    };

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
