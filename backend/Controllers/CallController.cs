using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SayimLink.Api.Controllers;

/// <summary>
/// WebRTC ICE sunucu konfigürasyonunu döner. STUN her zaman var (Google public).
/// TURN credential'ları env var ile gelir; TURN_URL boşsa istemci sadece STUN ile dener.
/// </summary>
[ApiController]
[Authorize]
[Route("api/call")]
public sealed class CallController : ControllerBase
{
    private readonly IConfiguration _config;

    public CallController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("ice-servers")]
    public IActionResult GetIceServers()
    {
        var iceServers = new List<object>
        {
            new { urls = new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" } },
        };

        var turnUrl = _config["Webrtc:TurnUrl"] ?? Environment.GetEnvironmentVariable("WEBRTC_TURN_URL");
        var turnUser = _config["Webrtc:TurnUsername"] ?? Environment.GetEnvironmentVariable("WEBRTC_TURN_USERNAME");
        var turnCred = _config["Webrtc:TurnCredential"] ?? Environment.GetEnvironmentVariable("WEBRTC_TURN_CREDENTIAL");

        if (!string.IsNullOrWhiteSpace(turnUrl))
        {
            iceServers.Add(new
            {
                urls = turnUrl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                username = turnUser,
                credential = turnCred,
            });
        }

        return Ok(new { iceServers });
    }
}
