using Microsoft.AspNetCore.Mvc;
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

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var mongoOk = await _mongo.PingAsync(cancellationToken);
        var payload = new
        {
            status = mongoOk ? "healthy" : "degraded",
            service = "sayimlink-api",
            version = "0.1.0",
            timestamp = DateTime.UtcNow,
            checks = new
            {
                mongo = mongoOk ? "up" : "down"
            }
        };

        return mongoOk ? Ok(payload) : StatusCode(503, payload);
    }
}
