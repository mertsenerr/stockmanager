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
[Authorize] // List/Get tüm authenticated; Create/Update/Delete action-level AdminLevel.
[Route("api/firmalar")]
public sealed class FirmalarController : ControllerBase
{
    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private readonly IFirmaRepository _firmalar;
    private readonly IMagazaRepository _magazalar;
    private readonly IAuditService _audit;
    private readonly IValidator<FirmaUpsertRequest> _validator;

    public FirmalarController(
        IFirmaRepository firmalar,
        IMagazaRepository magazalar,
        IAuditService audit,
        IValidator<FirmaUpsertRequest> validator)
    {
        _firmalar = firmalar;
        _magazalar = magazalar;
        _audit = audit;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var firmalar = await _firmalar.ListAsync(includeInactive, ct);
        // Phase 3: SayimBaskani only sees firmas they created. Sistem keeps full visibility.
        if (!User.IsSistem())
        {
            var uid = User.GetUserId();
            if (string.IsNullOrEmpty(uid)) return Ok(Array.Empty<FirmaDto>());
            firmalar = firmalar.Where(f => f.OlusturanKullaniciId == uid).ToList();
        }
        return Ok(firmalar.Select(ToDto));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var firma = await _firmalar.FindByIdAsync(id, ct);
        if (firma is null) return NotFound();
        // Phase 3: non-Sistem must own the firma.
        if (!User.IsSistem() && firma.OlusturanKullaniciId != User.GetUserId()) return Forbid();
        return Ok(ToDto(firma));
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Create([FromBody] FirmaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var uid = User.GetUserId();
        if (string.IsNullOrEmpty(uid)) return Unauthorized();

        // Phase 3.1: uniqueness scoped to caller's own catalog. Two SayimBaskanis can
        // each keep their own client firma "LCW" — they audit it separately.
        if (await _firmalar.AdExistsForOwnerAsync(request.Ad, uid, null, ct))
            return Conflict(new { message = "Bu ad ile zaten bir firmanız var." });

        var kisaltma = string.IsNullOrWhiteSpace(request.Kisaltma)
            ? string.Empty : request.Kisaltma.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(kisaltma) && await _firmalar.KisaltmaExistsForOwnerAsync(kisaltma, uid, null, ct))
            return Conflict(new { message = "Bu kısaltmayı zaten kullanıyorsunuz." });

        var firma = new Firma
        {
            Ad = request.Ad.Trim(),
            Kisaltma = kisaltma,
            Tip = request.Tip,
            LogoUrl = request.LogoUrl,
            AktifMi = request.AktifMi,
            OlusturanKullaniciId = uid,
        };
        await _firmalar.InsertAsync(firma, ct);
        _audit.Log(User, AuditAksiyonlari.FirmaCreate, "firma", firma.Id, yeni: firma.Ad);
        return CreatedAtAction(nameof(Get), new { id = firma.Id }, ToDto(firma));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> Update(string id, [FromBody] FirmaUpsertRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var firma = await _firmalar.FindByIdAsync(id, ct);
        if (firma is null) return NotFound();
        // Phase 3: non-Sistem may only update firmas they created.
        if (!User.IsSistem() && firma.OlusturanKullaniciId != User.GetUserId()) return Forbid();

        // Phase 3.1: rename uniqueness check stays scoped to the caller's own catalog so
        // a rename can't accidentally produce two same-named firmas under one owner, but
        // can still collide with a different SayimBaskani's identically-named record.
        var ownerForCheck = firma.OlusturanKullaniciId ?? string.Empty;
        if (!string.Equals(firma.Ad, request.Ad, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(ownerForCheck)
            && await _firmalar.AdExistsForOwnerAsync(request.Ad, ownerForCheck, id, ct))
            return Conflict(new { message = "Bu ad ile zaten bir firmanız var." });

        var oldName = firma.Ad;
        var newKisaltma = string.IsNullOrWhiteSpace(request.Kisaltma)
            ? firma.Kisaltma : request.Kisaltma.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(newKisaltma) && newKisaltma != firma.Kisaltma
            && !string.IsNullOrEmpty(ownerForCheck)
            && await _firmalar.KisaltmaExistsForOwnerAsync(newKisaltma, ownerForCheck, id, ct))
            return Conflict(new { message = "Bu kısaltmayı zaten kullanıyorsunuz." });

        firma.Ad = request.Ad.Trim();
        firma.Kisaltma = newKisaltma;
        firma.Tip = request.Tip;
        firma.LogoUrl = request.LogoUrl;
        firma.AktifMi = request.AktifMi;
        await _firmalar.ReplaceAsync(firma, ct);
        _audit.Log(User, AuditAksiyonlari.FirmaUpdate, "firma", firma.Id, eski: oldName, yeni: firma.Ad);

        return Ok(ToDto(firma));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminLevel)]
    public async Task<IActionResult> SoftDelete(string id, CancellationToken ct)
    {
        var firma = await _firmalar.FindByIdAsync(id, ct);
        if (firma is null) return NotFound();
        // Phase 3: non-Sistem may only delete firmas they created.
        if (!User.IsSistem() && firma.OlusturanKullaniciId != User.GetUserId()) return Forbid();

        if (await _magazalar.AnyForFirmaAsync(id, ct))
            return Conflict(new { message = "Firmaya bağlı aktif mağazalar var. Önce mağazaları silin." });

        await _firmalar.SoftDeleteAsync(id, ct);
        _audit.Log(User, AuditAksiyonlari.FirmaDelete, "firma", id, eski: firma.Ad);
        return NoContent();
    }

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }

    private static FirmaDto ToDto(Firma f) => new()
    {
        Id = f.Id,
        Ad = f.Ad,
        Kisaltma = f.Kisaltma,
        Tip = f.Tip,
        LogoUrl = f.LogoUrl,
        AktifMi = f.AktifMi,
        OlusturmaTarihi = f.OlusturmaTarihi,
    };
}
