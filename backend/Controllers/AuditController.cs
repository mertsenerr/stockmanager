using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Common;
using SayimLink.Api.Dtos.Rapor;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.AdminLevel)]
[Route("api/audit")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditLogRepository _logs;
    private readonly IUserRepository _users;

    public AuditController(IAuditLogRepository logs, IUserRepository users)
    {
        _logs = logs;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? kullaniciId,
        [FromQuery] string? aksiyon,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (take is < 1 or > 200) take = 50;
        if (skip < 0) skip = 0;

        // C-1: SayimBaskani sees only audit rows whose KullaniciId belongs to a user in the
        // same firma. We resolve the allow-set up front (one users.ListAsync) and pass either
        // the requested kullaniciId (verified against the set) or no filter (then post-filter).
        // Sistem keeps the unrestricted view.
        HashSet<string>? allowedUserIds = null;
        if (!User.IsSistem())
        {
            var firmaId = User.GetFirmaId();
            if (string.IsNullOrEmpty(firmaId))
                return Ok(EmptyPage(skip, take));

            var all = await _users.ListAsync(includeInactive: true, ct);
            allowedUserIds = all
                .Where(u => u.FirmaId == firmaId || u.FirmaIds.Contains(firmaId))
                .Select(u => u.Id)
                .ToHashSet();

            if (!string.IsNullOrEmpty(kullaniciId) && !allowedUserIds.Contains(kullaniciId))
                return Ok(EmptyPage(skip, take));
        }

        var (items, total) = await _logs.QueryAsync(
            TryParseDate(from), TryParseDate(to), kullaniciId, aksiyon, skip, take, ct);

        if (allowedUserIds is not null)
        {
            items = items.Where(l => l.KullaniciId is not null && allowedUserIds.Contains(l.KullaniciId)).ToList();
            total = items.Count; // approximate — total reflects post-filter visible rows
        }

        return Ok(new AuditPageDto
        {
            Items = items.Select(l => new AuditLogDto
            {
                Id = l.Id,
                Tarih = l.Tarih,
                KullaniciId = l.KullaniciId,
                KullaniciAdi = l.KullaniciAdi,
                KullaniciRol = l.KullaniciRol,
                Aksiyon = l.Aksiyon,
                Hedef = l.Hedef,
                HedefId = l.HedefId,
                EskiDeger = l.EskiDeger,
                YeniDeger = l.YeniDeger,
                IpAdres = l.IpAdres,
                Basarili = l.Basarili,
            }).ToList(),
            Total = total,
            Skip = skip,
            Take = take,
        });
    }

    private static AuditPageDto EmptyPage(int skip, int take) => new()
    {
        Items = new List<AuditLogDto>(),
        Total = 0,
        Skip = skip,
        Take = take,
    };

    private static DateTime? TryParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
    }
}
