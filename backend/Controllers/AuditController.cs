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
    private readonly IFirmaRepository _firmalar;

    public AuditController(IAuditLogRepository logs, IUserRepository users, IFirmaRepository firmalar)
    {
        _logs = logs;
        _users = users;
        _firmalar = firmalar;
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

        // Phase 2.5: SayimBaskani sees only logs for themselves + users attached to a Firma
        // they own. Sistem keeps the unrestricted view. The allowed-id set is pushed down
        // to the repository so paging totals reflect what the caller can actually see,
        // not just what the current page rendered.
        IReadOnlyCollection<string>? allowedUserIds = null;
        if (!User.IsSistem())
        {
            var uid = User.GetUserId();
            if (string.IsNullOrEmpty(uid))
                return Ok(EmptyPage(skip, take));

            var ownedFirmas = await _firmalar.ListOwnedOrgFirmasByAsync(uid, ct);
            var ownedFirmaIds = ownedFirmas.Select(f => f.Id).ToHashSet();

            var all = await _users.ListAsync(includeInactive: true, ct);
            var ids = new HashSet<string> { uid };
            foreach (var u in all)
            {
                if (!string.IsNullOrEmpty(u.FirmaId) && ownedFirmaIds.Contains(u.FirmaId))
                    ids.Add(u.Id);
            }
            allowedUserIds = ids;

            if (!string.IsNullOrEmpty(kullaniciId) && !ids.Contains(kullaniciId))
                return Ok(EmptyPage(skip, take));
        }

        var (items, total) = await _logs.QueryAsync(
            TryParseDate(from), TryParseDate(to), kullaniciId, aksiyon, skip, take,
            allowedUserIds, ct);

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
