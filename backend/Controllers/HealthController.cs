using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly IMongoDbService _mongo;

    public HealthController(IMongoDbService mongo)
    {
        _mongo = mongo;
    }

    // Public liveness probe — used by Render's health check + UptimeRobot. The
    // response is intentionally minimal: no version, no internal service name,
    // no downstream component status. Anything an attacker could fingerprint
    // (build version, framework banner, dependency health) lives behind the
    // admin-only detailed endpoint below.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var mongoOk = await _mongo.PingAsync(cancellationToken);
        return mongoOk
            ? Ok(new { status = "ok" })
            : StatusCode(503, new { status = "degraded" });
    }

    // Detailed health — same data the old public response used to expose, now
    // gated behind admin auth so platform operators can still see it via the
    // admin UI or curl with a token, but bots scraping /api/health get nothing.
    [HttpGet("details")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Details(CancellationToken cancellationToken)
    {
        var mongoOk = await _mongo.PingAsync(cancellationToken);
        var payload = new
        {
            status = mongoOk ? "healthy" : "degraded",
            timestamp = DateTime.UtcNow,
            checks = new
            {
                mongo = mongoOk ? "up" : "down"
            }
        };

        return mongoOk ? Ok(payload) : StatusCode(503, payload);
    }
}
