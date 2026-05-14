namespace SayimLink.Api.Dtos.Auth;

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string? TurnstileToken { get; set; }
}

public sealed class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class ResendVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}
