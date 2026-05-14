namespace SayimLink.Api.Configuration;

public sealed class TurnstileSettings
{
    public const string SectionName = "Turnstile";

    /// <summary>When false, the backend skips token verification entirely.
    /// Useful for local dev where you don't want CAPTCHA blocking every login.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Cloudflare-issued site key (frontend reads this from /api/auth/captcha/config).</summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>Server-side secret used when calling Cloudflare's siteverify API.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
