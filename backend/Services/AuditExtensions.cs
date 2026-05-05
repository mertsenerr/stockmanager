using System.Security.Claims;

namespace SayimLink.Api.Services;

public static class AuditExtensions
{
    public static void Log(
        this IAuditService audit,
        ClaimsPrincipal user,
        string aksiyon,
        string? hedef = null,
        string? hedefId = null,
        string? eski = null,
        string? yeni = null,
        string? ip = null,
        bool basarili = true)
    {
        audit.Enqueue(audit.Build(
            aksiyon,
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            user.FindFirst(ClaimTypes.Name)?.Value,
            user.FindFirst(ClaimTypes.Role)?.Value,
            hedef: hedef, hedefId: hedefId,
            eskiDeger: eski, yeniDeger: yeni,
            ip: ip, basarili: basarili));
    }
}
