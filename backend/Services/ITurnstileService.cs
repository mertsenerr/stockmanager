using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface ITurnstileService
{
    bool Enabled { get; }
    string SiteKey { get; }

    /// <summary>Verifies a Turnstile token via Cloudflare's siteverify API.
    /// Returns true if disabled (skip), false on any verification failure.</summary>
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}

public sealed class TurnstileService : ITurnstileService
{
    private readonly TurnstileSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TurnstileService> _logger;

    public TurnstileService(
        IOptions<TurnstileSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<TurnstileService> logger)
    {
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool Enabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.SecretKey);
    public string SiteKey => _settings.SiteKey;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (!Enabled) return true;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new Dictionary<string, string>
            {
                ["secret"] = _settings.SecretKey,
                ["response"] = token,
            };
            if (!string.IsNullOrWhiteSpace(remoteIp))
                payload["remoteip"] = remoteIp;

            var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(payload), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile siteverify HTTP {Status}", response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(cancellationToken: ct);
            if (body is null || !body.Success)
            {
                _logger.LogInformation("Turnstile verification failed: {Codes}",
                    body?.ErrorCodes is null ? "" : string.Join(",", body.ErrorCodes));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Turnstile verification call threw");
            return false;
        }
    }

    private sealed class TurnstileVerifyResponse
    {
        public bool Success { get; set; }
        public string[]? ErrorCodes { get; set; }
    }
}
