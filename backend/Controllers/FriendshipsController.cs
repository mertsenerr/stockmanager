using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/arkadaslar")]
public sealed class FriendshipsController : ControllerBase
{
    private readonly IFriendshipRepository _friends;
    private readonly IUserRepository _users;

    public FriendshipsController(IFriendshipRepository friends, IUserRepository users)
    {
        _friends = friends;
        _users = users;
    }

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();

        var accepted = await _friends.ListAcceptedForUserAsync(uid, ct);
        var incoming = await _friends.ListIncomingPendingAsync(uid, ct);
        var outgoing = await _friends.ListOutgoingPendingAsync(uid, ct);

        var allUserIds = accepted.SelectMany(f => new[] { f.FromUserId, f.ToUserId })
            .Concat(incoming.SelectMany(f => new[] { f.FromUserId, f.ToUserId }))
            .Concat(outgoing.SelectMany(f => new[] { f.FromUserId, f.ToUserId }))
            .Distinct()
            .ToList();

        var users = (await _users.ListByIdsAsync(allUserIds, ct))
            .ToDictionary(u => u.Id, u => u);

        FriendDto Build(Friendship f)
        {
            var otherId = f.FromUserId == uid ? f.ToUserId : f.FromUserId;
            users.TryGetValue(otherId, out var u);
            return new FriendDto(
                f.Id,
                otherId,
                u?.AdSoyad ?? "?",
                u?.Email ?? "",
                u?.Rol ?? "",
                f.Durum,
                f.FromUserId == uid);
        }

        return Ok(new
        {
            arkadaslar = accepted.Select(Build).OrderBy(d => d.AdSoyad).ToList(),
            gelenIstekler = incoming.Select(Build).OrderByDescending(_ => _.Id).ToList(),
            gidenIstekler = outgoing.Select(Build).OrderByDescending(_ => _.Id).ToList(),
        });
    }

    [HttpGet("ara")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<UserSearchDto>());

        var needle = q.Trim().ToLowerInvariant();
        var all = await _users.ListAsync(includeInactive: false, ct);

        // Mevcut arkadaşlık ilişkilerini bir kez çek — N+1 önlemek için.
        var allFriendships = (await _friends.ListAcceptedForUserAsync(uid, ct))
            .Concat(await _friends.ListIncomingPendingAsync(uid, ct))
            .Concat(await _friends.ListOutgoingPendingAsync(uid, ct))
            .ToList();

        string StatusFor(string otherId)
        {
            var f = allFriendships.FirstOrDefault(x =>
                (x.FromUserId == uid && x.ToUserId == otherId) ||
                (x.FromUserId == otherId && x.ToUserId == uid));
            if (f is null) return "yok";
            if (f.Durum == FriendshipDurumlari.Kabul) return "arkadas";
            if (f.Durum == FriendshipDurumlari.Beklemede)
                return f.FromUserId == uid ? "giden" : "gelen";
            return "yok";
        }

        var matches = all
            .Where(u => u.Id != uid)
            .Where(u =>
                u.AdSoyad.ToLowerInvariant().Contains(needle) ||
                u.Email.ToLowerInvariant().Contains(needle))
            .Take(20)
            .Select(u => new UserSearchDto(u.Id, u.AdSoyad, u.Email, u.Rol, StatusFor(u.Id)))
            .ToList();

        return Ok(matches);
    }

    [HttpPost("istek")]
    public async Task<IActionResult> SendRequest([FromBody] FriendRequestBody body, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        if (string.IsNullOrEmpty(body?.ToUserId) || body.ToUserId == uid)
            return BadRequest(new { message = "Hedef kullanıcı geçersiz." });

        var target = await _users.FindByIdAsync(body.ToUserId, ct);
        if (target is null || !target.AktifMi)
            return NotFound(new { message = "Kullanıcı bulunamadı." });

        var existing = await _friends.FindBetweenAsync(uid, body.ToUserId, ct);
        if (existing is not null)
        {
            if (existing.Durum == FriendshipDurumlari.Kabul)
                return Conflict(new { message = "Zaten arkadaşsınız." });
            if (existing.Durum == FriendshipDurumlari.Beklemede)
                return Conflict(new { message = "Zaten bekleyen bir istek var." });
            // Reddedildiyse yeni isteğe izin ver — kaydı yeniden açıyoruz.
            existing.FromUserId = uid;
            existing.ToUserId = body.ToUserId;
            existing.Durum = FriendshipDurumlari.Beklemede;
            existing.OlusturmaTarihi = DateTime.UtcNow;
            existing.KararTarihi = null;
            await _friends.ReplaceAsync(existing, ct);
            return Ok(new { id = existing.Id });
        }

        var f = new Friendship
        {
            FromUserId = uid,
            ToUserId = body.ToUserId,
            Durum = FriendshipDurumlari.Beklemede,
        };
        await _friends.InsertAsync(f, ct);
        return Ok(new { id = f.Id });
    }

    [HttpPost("istek/{id}/kabul")]
    public async Task<IActionResult> Accept(string id, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var f = await _friends.FindByIdAsync(id, ct);
        if (f is null) return NotFound();
        if (f.ToUserId != uid) return Forbid();
        if (f.Durum != FriendshipDurumlari.Beklemede)
            return Conflict(new { message = "İstek zaten karara bağlanmış." });
        f.Durum = FriendshipDurumlari.Kabul;
        f.KararTarihi = DateTime.UtcNow;
        await _friends.ReplaceAsync(f, ct);
        return NoContent();
    }

    [HttpPost("istek/{id}/red")]
    public async Task<IActionResult> Reject(string id, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var f = await _friends.FindByIdAsync(id, ct);
        if (f is null) return NotFound();
        if (f.ToUserId != uid) return Forbid();
        if (f.Durum != FriendshipDurumlari.Beklemede)
            return Conflict(new { message = "İstek zaten karara bağlanmış." });
        f.Durum = FriendshipDurumlari.Red;
        f.KararTarihi = DateTime.UtcNow;
        await _friends.ReplaceAsync(f, ct);
        return NoContent();
    }

    [HttpDelete("{otherUserId}")]
    public async Task<IActionResult> Remove(string otherUserId, CancellationToken ct)
    {
        var uid = Uid;
        if (uid is null) return Unauthorized();
        var f = await _friends.FindBetweenAsync(uid, otherUserId, ct);
        if (f is null) return NotFound();
        await _friends.DeleteAsync(f.Id, ct);
        return NoContent();
    }
}

public sealed record FriendRequestBody(string ToUserId);
public sealed record FriendDto(string Id, string KullaniciId, string AdSoyad, string Email, string Rol, string Durum, bool Giden);
public sealed record UserSearchDto(string Id, string AdSoyad, string Email, string Rol, string ArkadaslikDurumu);
