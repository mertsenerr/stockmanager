using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[Route("api/kullanicilar")]
public sealed class KullanicilarController : AdminControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IValidator<KullaniciCreateRequest> _createValidator;
    private readonly IValidator<KullaniciUpdateRequest> _updateValidator;

    public KullanicilarController(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IAuditService audit,
        IValidator<KullaniciCreateRequest> createValidator,
        IValidator<KullaniciUpdateRequest> updateValidator)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _audit = audit;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var users = await _users.ListAsync(includeInactive, ct);
        return Ok(users.Select(ToListDto));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id, ct);
        return user is null ? NotFound() : Ok(ToListDto(user));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KullaniciCreateRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var existing = await _users.FindByEmailAsync(request.Email, ct);
        if (existing is not null)
            return Conflict(new { message = "Bu email ile bir kullanıcı zaten var." });

        var user = new User
        {
            Email = request.Email.ToLowerInvariant().Trim(),
            AdSoyad = request.AdSoyad.Trim(),
            Rol = request.Rol,
            PasswordHash = _hasher.Hash(request.Password),
            FirmaIds = request.FirmaIds.Distinct().ToList(),
            MagazaIds = request.MagazaIds.Distinct().ToList(),
            AktifMi = request.AktifMi,
        };
        await _users.InsertAsync(user, ct);
        _audit.Log(User, AuditAksiyonlari.KullaniciCreate, "kullanici", user.Id, yeni: $"{user.Email} · {user.Rol}");
        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToListDto(user));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KullaniciUpdateRequest request, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        var user = await _users.FindByIdAsync(id, ct);
        if (user is null) return NotFound();

        // Last admin protection
        if (user.Rol == Roles.Admin && request.Rol != Roles.Admin)
        {
            var adminCount = await _users.CountActiveAdminsAsync(ct);
            if (adminCount <= 1)
                return Conflict(new { message = "Sistemdeki son admin'i başka role düşüremezsiniz." });
        }
        if (user.Rol == Roles.Admin && !request.AktifMi && user.AktifMi)
        {
            var adminCount = await _users.CountActiveAdminsAsync(ct);
            if (adminCount <= 1)
                return Conflict(new { message = "Sistemdeki son admin'i pasifleştiremezsiniz." });
        }
        if (id == CurrentUserId && request.Rol != Roles.Admin && user.Rol == Roles.Admin)
            return Conflict(new { message = "Kendi rolünüzü düşüremezsiniz." });

        user.AdSoyad = request.AdSoyad.Trim();
        user.Rol = request.Rol;
        user.FirmaIds = request.FirmaIds.Distinct().ToList();
        user.MagazaIds = request.MagazaIds.Distinct().ToList();
        user.AktifMi = request.AktifMi;
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
            user.PasswordHash = _hasher.Hash(request.NewPassword);

        await _users.ReplaceAsync(user, ct);

        // Sessions are invalidated when role/active changes, OR password reset.
        if (!string.IsNullOrWhiteSpace(request.NewPassword) || !user.AktifMi)
            await _refreshTokens.RevokeAllForUserAsync(user.Id, "admin_update", ct);

        _audit.Log(User, AuditAksiyonlari.KullaniciUpdate, "kullanici", user.Id,
            yeni: $"{user.Rol}{(string.IsNullOrEmpty(request.NewPassword) ? "" : " (parola sıfırlandı)")}");
        return Ok(ToListDto(user));
    }

    [HttpGet("pending")]
    public async Task<IActionResult> ListPending(CancellationToken ct)
    {
        // Sistem (süper admin) tüm pending'leri görür; SayimBaskani sadece kendi firmasının onay bekleyenlerini.
        if (User.IsInRole(Roles.Sistem))
        {
            var all = await _users.ListAsync(includeInactive: false, ct);
            return Ok(all.Where(u => !u.Onayli).Select(ToListDto));
        }

        var firmaId = User.FindFirst("firmaId")?.Value
                      ?? User.FindFirst("firmaIds")?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(firmaId))
            return Ok(Array.Empty<KullaniciListDto>());
        var pending = await _users.ListPendingForFirmaAsync(firmaId, ct);
        return Ok(pending.Select(ToListDto));
    }

    [HttpPatch("{id}/approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id, ct);
        if (user is null) return NotFound();
        if (user.Onayli) return Ok(ToListDto(user));

        user.Onayli = true;
        await _users.ReplaceAsync(user, ct);
        _audit.Log(User, AuditAksiyonlari.KullaniciUpdate, "kullanici", user.Id, yeni: $"approved · {user.Email}");
        return Ok(ToListDto(user));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDelete(string id, CancellationToken ct)
    {
        if (id == CurrentUserId)
            return Conflict(new { message = "Kendi hesabınızı silemezsiniz." });

        var user = await _users.FindByIdAsync(id, ct);
        if (user is null) return NotFound();

        if (user.Rol == Roles.Admin)
        {
            var adminCount = await _users.CountActiveAdminsAsync(ct);
            if (adminCount <= 1)
                return Conflict(new { message = "Sistemdeki son admin'i silemezsiniz." });
        }

        await _users.SoftDeleteAsync(id, ct);
        await _refreshTokens.RevokeAllForUserAsync(id, "deleted", ct);
        _audit.Log(User, AuditAksiyonlari.KullaniciDelete, "kullanici", id, eski: user.Email);
        return NoContent();
    }

    private static KullaniciListDto ToListDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        AdSoyad = u.AdSoyad,
        Rol = u.Rol,
        FirmaId = u.FirmaId,
        FirmaIds = u.FirmaIds,
        MagazaIds = u.MagazaIds,
        AktifMi = u.AktifMi,
        Onayli = u.Onayli,
        SonGirisTarihi = u.SonGirisTarihi,
        OlusturmaTarihi = u.OlusturmaTarihi,
    };
}
