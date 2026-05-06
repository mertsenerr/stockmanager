namespace SayimLink.Api.Configuration;

public sealed class ResendSettings
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    // Defaults intentionally empty — production must set these via env vars.
    // FromEmail must be a verified address on a Resend-verified domain.
    // PasswordResetUrlTemplate must contain the literal "{token}" placeholder.
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "SayımLink";
    public string PasswordResetUrlTemplate { get; set; } = string.Empty;
    public string EmailVerificationUrlTemplate { get; set; } = string.Empty;
}
