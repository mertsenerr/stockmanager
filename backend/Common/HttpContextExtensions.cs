using System.Net;

namespace SayimLink.Api.Common;

public static class HttpContextExtensions
{
    private const string AuditIpItem = "AuditClientIp";

    /// <summary>Caches the value resolved by <see cref="ResolveAuditClientIp"/>.
    /// Pipeline middleware sets it once per request, controllers / audit logging
    /// read it via <see cref="ClientIpForAudit"/>. Kept off
    /// HttpContext.Connection.RemoteIpAddress on purpose — the rate limiter and
    /// SignalR transport must keep seeing the real upstream IP so a spoofed
    /// <c>CF-Connecting-IP</c> header can't partition the limiter into infinite
    /// buckets.</summary>
    public static void CacheAuditClientIp(this HttpContext ctx, string? ip)
    {
        if (!string.IsNullOrWhiteSpace(ip))
            ctx.Items[AuditIpItem] = ip;
    }

    /// <summary>Prefer-CF-then-transport client IP for audit logs and the
    /// "active sessions" UI. NEVER use this for any security decision — the
    /// CF-Connecting-IP header is attacker-controlled when an attacker hits the
    /// origin URL directly (Render's origin URL is public).</summary>
    public static string? ClientIpForAudit(this HttpContext ctx)
    {
        if (ctx.Items[AuditIpItem] is string cached) return cached;
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>Reads the inbound <c>CF-Connecting-IP</c> header and caches it
    /// for downstream <see cref="ClientIpForAudit"/> calls. Returns true when a
    /// CF IP was present and well-formed.</summary>
    public static bool ResolveAuditClientIp(this HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfRaw)
            && IPAddress.TryParse(cfRaw.ToString().Trim(), out var cfIp))
        {
            ctx.CacheAuditClientIp(cfIp.ToString());
            return true;
        }
        return false;
    }
}
