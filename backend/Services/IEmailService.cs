using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string toName, string resetUrl, CancellationToken ct = default);
    Task SendEmailVerificationAsync(string toEmail, string toName, string verifyUrl, CancellationToken ct = default);
    Task SendTwoFactorCodeAsync(string toEmail, string toName, string code, CancellationToken ct = default);
    Task SendPasswordChangedAsync(string toEmail, string toName, string undoUrl, CancellationToken ct = default);
}

public sealed class ResendEmailService : IEmailService
{
    private readonly ResendSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IOptions<ResendSettings> options,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<ResendEmailService> logger)
    {
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>HTML-encode any user-controlled string before splicing it into an
    /// email body. Without this, a user who registers with <c>AdSoyad =
    /// &lt;a href="evil"&gt;click&lt;/a&gt;</c> can inject arbitrary markup
    /// (phishing links, fake call-to-action buttons) into every system mail their
    /// account receives — and admins who create users for others would spray the
    /// same payload at the real victims.</summary>
    private static string Esc(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : WebUtility.HtmlEncode(s);

    public async Task SendPasswordResetAsync(
        string toEmail,
        string toName,
        string resetUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            if (_environment.IsDevelopment())
            {
                // Dev convenience: surface the link so a developer can finish the flow
                // without a real Resend key. Never reached in Production.
                _logger.LogWarning(
                    "Resend API key not configured — password reset email skipped. Reset URL for {Email}: {Url}",
                    toEmail, resetUrl);
                return;
            }

            _logger.LogError(
                "Resend API key not configured — password reset email NOT sent for {Email}.",
                toEmail);
            throw new InvalidOperationException(
                "Email service is not configured. Set Resend__ApiKey on the host.");
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
                Merhaba {Esc(toName)}, parolanı sıfırlamak için aşağıdaki bağlantıya tıkla.
                Bağlantı 30 dakika geçerlidir.
              </p>
              <a href="{Esc(resetUrl)}"
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

    public async Task SendPasswordChangedAsync(
        string toEmail,
        string toName,
        string undoUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "Resend API key not configured — password-changed email skipped. Undo URL for {Email}: {Url}",
                toEmail, undoUrl);
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var html =
            $"""
            <div style="font-family: -apple-system, Inter, sans-serif; max-width: 480px; margin: 24px auto; color: #fafafa; background: #111111; padding: 32px; border: 1px solid #1f1f1f; border-radius: 12px;">
              <h2 style="font-size: 18px; margin: 0 0 16px;">SynCompare — Parola değişti</h2>
              <p style="font-size: 14px; color: #a1a1aa; line-height: 1.5;">
                Merhaba {Esc(toName)}, hesabının parolası az önce değiştirildi. Bu sen değilsen
                hemen aşağıdaki bağlantıya tıklayarak değişikliği geri al ve diğer cihazlardan
                çıkış yapılsın. Bağlantı 30 dakika geçerlidir.
              </p>
              <a href="{Esc(undoUrl)}"
                 style="display: inline-block; margin-top: 16px; padding: 10px 16px; background: #ff5d3a; color: #fff; text-decoration: none; border-radius: 6px; font-size: 14px; font-weight: 600;">
                Bu ben değildim — geri al
              </a>
              <p style="font-size: 12px; color: #71717a; margin-top: 24px;">
                Bu işlemi sen yaptıysan bu e-postayı yok sayabilirsin.
              </p>
            </div>
            """;

        var payload = new
        {
            from = $"{_settings.FromName} <{_settings.FromEmail}>",
            to = new[] { toEmail },
            subject = "SynCompare — Parola değiştirildi",
            html,
        };
        try
        {
            var response = await client.PostAsJsonAsync("emails", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Resend password-changed send failed for {Email}: {Status} {Body}",
                    toEmail, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend exception while sending password-changed notice to {Email}", toEmail);
        }
    }

    public async Task SendTwoFactorCodeAsync(
        string toEmail,
        string toName,
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "Resend API key not configured — 2FA code email skipped. Code for {Email}: {Code}",
                toEmail, code);
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var html =
            $"""
            <div style="font-family: -apple-system, Inter, sans-serif; max-width: 480px; margin: 24px auto; color: #fafafa; background: #111111; padding: 32px; border: 1px solid #1f1f1f; border-radius: 12px;">
              <h2 style="font-size: 18px; margin: 0 0 16px;">SynCompare — Doğrulama Kodu</h2>
              <p style="font-size: 14px; color: #a1a1aa; line-height: 1.5;">
                Merhaba {Esc(toName)}, giriş işlemini tamamlamak için doğrulama kodu:
              </p>
              <p style="font-family: 'JetBrains Mono', monospace; font-size: 32px; font-weight: 700; letter-spacing: 0.32em; text-align: center; padding: 18px; background: #0a0a0a; border-radius: 8px; color: #14b8a6; margin: 16px 0;">
                {code}
              </p>
              <p style="font-size: 12px; color: #71717a;">
                Kod 10 dakika geçerlidir. Bu girişi sen yapmadıysan parolanı hemen değiştir.
              </p>
            </div>
            """;

        var payload = new
        {
            from = $"{_settings.FromName} <{_settings.FromEmail}>",
            to = new[] { toEmail },
            // Kod artık sadece body'de — kilit-ekranı bildirim önizlemesi (Gmail/iOS
            // banner, smartwatch, masa üstündeki telefon) OTP'yi 3. şahıslara açık
            // göstermesin. Subject "yapıyor musun?" sorusuna yeter.
            subject = "SynCompare doğrulama kodu",
            html,
        };
        try
        {
            var response = await client.PostAsJsonAsync("emails", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "Resend 2FA code send failed for {Email}: {Status} {Body}",
                    toEmail, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend exception while sending 2FA code to {Email}", toEmail);
        }
    }

    public async Task SendEmailVerificationAsync(
        string toEmail,
        string toName,
        string verifyUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Resend API key not configured — verification email skipped. Verify URL for {Email}: {Url}",
                    toEmail, verifyUrl);
                return;
            }

            _logger.LogError(
                "Resend API key not configured — verification email NOT sent for {Email}.",
                toEmail);
            throw new InvalidOperationException(
                "Email service is not configured. Set Resend__ApiKey on the host.");
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var html =
            $"""
            <div style="font-family: -apple-system, Inter, sans-serif; max-width: 480px; margin: 24px auto; color: #fafafa; background: #111111; padding: 32px; border: 1px solid #1f1f1f; border-radius: 12px;">
              <h2 style="font-size: 18px; margin: 0 0 16px;">SayımLink — E-posta Doğrulama</h2>
              <p style="font-size: 14px; color: #a1a1aa; line-height: 1.5;">
                Merhaba {Esc(toName)}, hesabını aktif etmek için aşağıdaki bağlantıya tıklayarak
                e-posta adresini doğrula. Bağlantı 24 saat geçerlidir.
              </p>
              <a href="{Esc(verifyUrl)}"
                 style="display: inline-block; margin-top: 16px; padding: 10px 16px; background: #fafafa; color: #0a0a0a; text-decoration: none; border-radius: 6px; font-size: 14px; font-weight: 500;">
                E-postamı doğrula
              </a>
              <p style="font-size: 12px; color: #71717a; margin-top: 24px;">
                Bu kaydı sen yapmadıysan bu maili yok sayabilirsin.
              </p>
            </div>
            """;

        var payload = new
        {
            from = $"{_settings.FromName} <{_settings.FromEmail}>",
            to = new[] { toEmail },
            subject = "SayımLink — E-posta adresinizi doğrulayın",
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
            _logger.LogError(ex, "Resend exception while sending verification email to {Email}", toEmail);
        }
    }
}
