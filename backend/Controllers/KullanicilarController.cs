using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Admin;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[Route("api/kullanicilar")]
public sealed class KullanicilarController : AdminControllerBase
{
    private readonly IUserRepository _users;
    private readonly IFirmaRepository _firmalar;
    private readonly IMagazaRepository _magazalar;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IValidator<KullaniciCreateRequest> _createValidator;
    private readonly IValidator<KullaniciUpdateRequest> _updateValidator;

    public KullanicilarController(
        IUserRepository users,
        IFirmaRepository firmalar,
        IMagazaRepository magazalar,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IAuditService audit,
        IValidator<KullaniciCreateRequest> createValidator,
        IValidator<KullaniciUpdateRequest> updateValidator)
    {
        _users = users;
        _firmalar = firmalar;
        _magazalar = magazalar;
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
        // Phase 2.5: caller sees themselves + every user attached to a Firma the caller owns.
        // Sistem keeps full visibility.
        var scoped = await ScopeToCallerOwnedFirmasAsync(users, ct);
        return Ok(scoped.Select(ToListDto));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id, ct);
        if (user is null) return NotFound();
        if (User.IsSistem()) return Ok(ToListDto(user));

        // Phase 2.5: self-fetch always allowed; otherwise the target user must live in a
        // Firma the caller owns.
        var uid = User.GetUserId();
        if (uid is null) return Forbid();
        if (user.Id == uid) return Ok(ToListDto(user));

        var ownedFirmaIds = await GetCallerOwnedFirmaIdsAsync(uid, ct);
        if (string.IsNullOrEmpty(user.FirmaId) || !ownedFirmaIds.Contains(user.FirmaId))
            return Forbid();

        return Ok(ToListDto(user));
    }

    // Phase 2.5 scoping helper — replaces the C-1 same-FirmaId filter.
    // "Users I see" = me + every active user attached to an organizasyon Firma I own.
    private async Task<IEnumerable<User>> ScopeToCallerOwnedFirmasAsync(
        IEnumerable<User> users, CancellationToken ct)
    {
        if (User.IsSistem()) return users;
        var uid = User.GetUserId();
        if (string.IsNullOrEmpty(uid)) return Array.Empty<User>();

        var ownedFirmaIds = await GetCallerOwnedFirmaIdsAsync(uid, ct);
        return users.Where(u =>
            u.Id == uid ||
            (!string.IsNullOrEmpty(u.FirmaId) && ownedFirmaIds.Contains(u.FirmaId)));
    }

    private async Task<HashSet<string>> GetCallerOwnedFirmaIdsAsync(string uid, CancellationToken ct)
    {
        var firmas = await _firmalar.ListOwnedOrgFirmasByAsync(uid, ct);
        return firmas.Select(f => f.Id).ToHashSet();
    }

    private async Task<HashSet<string>> GetCallerOwnedMagazaIdsAsync(
        HashSet<string> ownedFirmaIds, CancellationToken ct)
    {
        if (ownedFirmaIds.Count == 0) return [];
        var magazas = await _magazalar.ListAsync(firmaId: null, includeInactive: true, ct);
        return magazas.Where(m => ownedFirmaIds.Contains(m.FirmaId)).Select(m => m.Id).ToHashSet();
    }

    // Authorization gate for Update/Delete on a target user.
    // Sistem can touch anyone. Non-Sistem must either be acting on themselves OR the target's
    // primary FirmaId is in the caller's owned-org-firmas set. Returns the owned-firma ids
    // for downstream filtering so we don't refetch.
    private async Task<(bool canManage, HashSet<string> ownedFirmaIds)>
        CanManageAsync(User target, CancellationToken ct)
    {
        if (User.IsSistem()) return (true, new HashSet<string>());
        var uid = User.GetUserId();
        if (string.IsNullOrEmpty(uid)) return (false, new HashSet<string>());

        var owned = await GetCallerOwnedFirmaIdsAsync(uid, ct);
        if (target.Id == uid) return (true, owned);
        if (!string.IsNullOrEmpty(target.FirmaId) && owned.Contains(target.FirmaId))
            return (true, owned);
        return (false, owned);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KullaniciCreateRequest request, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationFailure(validation);

        // Role gate: only Sistem can mint another Sistem account. Anyone else picking the
        // Sistem role on the create form is a privilege-escalation attempt.
        if (!User.IsSistem() && request.Rol == Roles.Sistem)
            return Forbid();

        var existing = await _users.FindByEmailAsync(request.Email, ct);
        if (existing is not null)
            return Conflict(new { message = "Bu email ile bir kullanıcı zaten var." });

        var requestedFirmaIds = request.FirmaIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        var requestedMagazaIds = request.MagazaIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

        string? primaryFirmaId = null;
        List<string> firmaIds;
        List<string> magazaIds;

        if (User.IsSistem())
        {
            firmaIds = requestedFirmaIds;
            magazaIds = requestedMagazaIds;
            primaryFirmaId = firmaIds.FirstOrDefault();
        }
        else
        {
            // SayimBaskani may only attach the new user to firmas/magazas they own.
            // We refuse to create a "ghost" user with no owned firma — they would be invisible
            // to the caller on the next List() call and impossible to manage.
            var uid = User.GetUserId();
            if (string.IsNullOrEmpty(uid)) return Unauthorized();

            var ownedFirmaIds = await GetCallerOwnedFirmaIdsAsync(uid, ct);
            firmaIds = requestedFirmaIds.Where(ownedFirmaIds.Contains).ToList();
            if (firmaIds.Count == 0)
                return BadRequest(new { message = "Kullanıcıyı en az bir sahibi olduğunuz firmaya bağlamalısınız." });

            var ownedMagazaIds = await GetCallerOwnedMagazaIdsAsync(ownedFirmaIds, ct);
            magazaIds = requestedMagazaIds.Where(ownedMagazaIds.Contains).ToList();
            primaryFirmaId = firmaIds[0];
        }

        var user = new User
        {
            Email = request.Email.ToLowerInvariant().Trim(),
            AdSoyad = request.AdSoyad.Trim(),
            Rol = request.Rol,
            PasswordHash = _hasher.Hash(request.Password),
            FirmaId = primaryFirmaId,
            FirmaIds = firmaIds,
            MagazaIds = magazaIds,
            AktifMi = request.AktifMi,
        };
        try
        {
            await _users.InsertAsync(user, ct);
        }
        catch (DuplicateEmailException)
        {
            // Pre-check passed but the unique index rejected the insert — concurrent create
            // for the same email landed first. Translate to the same 409 the pre-check uses.
            return Conflict(new { message = "Bu email ile bir kullanıcı zaten var." });
        }
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

        var (canManage, ownedFirmaIds) = await CanManageAsync(user, ct);
        if (!canManage) return Forbid();

        // Reactivation collision guard. The email unique index is partial (only active
        // rows), so if someone else has registered the same address while this user was
        // soft-deleted, flipping AktifMi back on would otherwise throw a DuplicateKey
        // out of Mongo as a raw 500 — catch it here with a friendly 409 instead.
        if (!user.AktifMi && request.AktifMi)
        {
            var other = await _users.FindByEmailAsync(user.Email, ct);
            if (other is not null && other.Id != user.Id && other.AktifMi)
                return Conflict(new
                {
                    message = "Bu e-posta başka bir aktif kullanıcıda kullanılıyor. " +
                              "Eski kullanıcıyı yeniden aktif etmek için önce yeni kaydı silin.",
                });
        }

        // Privilege escalation guard: only Sistem can promote anyone (incl. themselves)
        // to the Sistem role, and only Sistem can demote an existing Sistem user.
        if (!User.IsSistem())
        {
            if (request.Rol == Roles.Sistem) return Forbid();
            if (user.Rol == Roles.Sistem) return Forbid();
        }

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

        var oldRol = user.Rol;
        var oldAktifMi = user.AktifMi;
        user.AdSoyad = request.AdSoyad.Trim();
        user.Rol = request.Rol;

        var requestedFirmaIds = request.FirmaIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        var requestedMagazaIds = request.MagazaIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();

        if (User.IsSistem())
        {
            user.FirmaIds = requestedFirmaIds;
            user.MagazaIds = requestedMagazaIds;
            user.AktifMi = request.AktifMi;
        }
        else
        {
            // SayimBaskani may only add/remove firmas they own. Memberships in firmas
            // owned by another SayimBaskani are preserved as-is so callers can't strip
            // a user out of someone else's tenancy. Same rule for magazalar.
            var ownedMagazaIds = await GetCallerOwnedMagazaIdsAsync(ownedFirmaIds, ct);

            var preservedFirmaIds = user.FirmaIds.Where(f => !ownedFirmaIds.Contains(f));
            var newFirmaIds = requestedFirmaIds.Where(ownedFirmaIds.Contains);
            user.FirmaIds = preservedFirmaIds.Concat(newFirmaIds).Distinct().ToList();

            var preservedMagazaIds = user.MagazaIds.Where(m => !ownedMagazaIds.Contains(m));
            var newMagazaIds = requestedMagazaIds.Where(ownedMagazaIds.Contains);
            user.MagazaIds = preservedMagazaIds.Concat(newMagazaIds).Distinct().ToList();

            // Active flag: caller may flip it only if the target lives entirely under the
            // caller's tenancy (i.e. has no membership in another SayimBaskani's firma).
            var hasOutsideMembership = user.FirmaIds.Any(f => !ownedFirmaIds.Contains(f));
            if (!hasOutsideMembership)
                user.AktifMi = request.AktifMi;
        }
        if (!string.IsNullOrWhiteSpace(request.NewPassword))
            user.PasswordHash = _hasher.Hash(request.NewPassword);

        await _users.ReplaceAsync(user, ct);

        // Sessions are invalidated when role/active changes, OR password reset.
        // RevokeAllForUserAsync handles refresh tokens; BumpTokenInvalidationAsync
        // pulls the access-token rug too so the admin update takes effect within
        // 30s (cache TTL) instead of waiting up to AccessTokenMinutes.
        var deactivated = oldAktifMi && !user.AktifMi;
        var roleChanged = oldRol != user.Rol;
        if (!string.IsNullOrWhiteSpace(request.NewPassword) || deactivated)
        {
            await _refreshTokens.RevokeAllForUserAsync(user.Id, "admin_update", ct);
            await _users.BumpTokenInvalidationAsync(user.Id, ct);
        }
        else if (roleChanged)
        {
            // Role change without password reset: still bump so the new role
            // takes effect immediately (otherwise the user keeps the old claims
            // until their next refresh).
            await _users.BumpTokenInvalidationAsync(user.Id, ct);
        }

        _audit.Log(User, AuditAksiyonlari.KullaniciUpdate, "kullanici", user.Id,
            yeni: $"{user.Rol}{(string.IsNullOrEmpty(request.NewPassword) ? "" : " (parola sıfırlandı)")}");
        return Ok(ToListDto(user));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDelete(string id, CancellationToken ct)
    {
        if (id == CurrentUserId)
            return Conflict(new { message = "Kendi hesabınızı silemezsiniz." });

        var user = await _users.FindByIdAsync(id, ct);
        if (user is null) return NotFound();

        var (canManage, _) = await CanManageAsync(user, ct);
        if (!canManage) return Forbid();

        // Non-Sistem can never delete a Sistem user even if (somehow) reachable.
        if (!User.IsSistem() && user.Rol == Roles.Sistem) return Forbid();

        if (user.Rol == Roles.Admin)
        {
            var adminCount = await _users.CountActiveAdminsAsync(ct);
            if (adminCount <= 1)
                return Conflict(new { message = "Sistemdeki son admin'i silemezsiniz." });
        }

        await _users.SoftDeleteAsync(id, ct);
        await _refreshTokens.RevokeAllForUserAsync(id, "deleted", ct);
        await _users.BumpTokenInvalidationAsync(id, ct);
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
        SonGirisTarihi = u.SonGirisTarihi,
        OlusturmaTarihi = u.OlusturmaTarihi,
    };
}
