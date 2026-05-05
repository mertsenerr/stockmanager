using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, CancellationToken ct = default);
}

public sealed class ResendEmailService : IEmailService
{
    private readonly ResendSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IOptions<ResendSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ResendEmailService> logger)
    {
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendPasswordResetAsync(
        string toEmail,
        string toName,
        string resetUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "Resend API key not configured — password reset email skipped. Reset URL for {Email}: {Url}",
                toEmail, resetUrl);
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var html =
            $"""
            <div style="font-family: -apple-system, Inter, sans-serif; max-width: 480px; margin: 24px auto; color: #fafafa; background: #111111; padding: 32px; border: 1px solid #1f1f1f; border-radius: 12px;">
              <h2 style="font-size: 18px; margin: 0 0 16px;">SayımLink — Parola Sıfırlama</h2>
              <p style="font-size: 14px; color: #a1a1aa; line-height: 1.5;">
                Merhaba {toName}, parolanı sıfırlamak için aşağıdaki bağlantıya tıkla.
                Bağlantı 30 dakika geçerlidir.
              </p>
              <a href="{resetUrl}"
                 style="display: inline-block; margin-top: 16px; padding: 10px 16px; background: #fafafa; color: #0a0a0a; text-decoration: none; border-radius: 6px; font-size: 14px; font-weight: 500;">
                Parolayı sıfırla
              </a>
              <p style="font-size: 12px; color: #71717a; margin-top: 24px;">
                Bu isteği sen yapmadıysan bu maili yok sayabilirsin.
              </p>
            </div>
            """;

        var payload = new
        {
            from = $"{_settings.FromName} <{_settings.FromEmail}>",
            to = new[] { toEmail },
            subject = "SayımLink — Parola sıfırlama",
            html,
        };

        try
        {
            var response = await client.PostAsJsonAsync("emails", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Resend send failed for {Email}: {Status} {Body}",
                    toEmail, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend exception while sending password reset to {Email}", toEmail);
        }
    }
}
