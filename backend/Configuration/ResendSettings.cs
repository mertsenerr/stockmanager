namespace SayimLink.Api.Configuration;

public sealed class ResendSettings
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "no-reply@sayimlink.local";
    public string FromName { get; set; } = "SayımLink";
    public string PasswordResetUrlTemplate { get; set; } = "http://localhost:4200/reset-password?token={token}";
}
