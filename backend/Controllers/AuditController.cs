using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public AuditController(IAuditLogRepository logs) { _logs = logs; }

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

        var (items, total) = await _logs.QueryAsync(
            TryParseDate(from), TryParseDate(to), kullaniciId, aksiyon, skip, take, ct);

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

    private static DateTime? TryParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
    }
}
