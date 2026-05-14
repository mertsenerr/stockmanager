namespace SayimLink.Api.Dtos.Auth;

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>Required when the user has any 2FA method enabled. One of:
    /// "totp" | "email" | "recovery". WebAuthn step-up isn't supported here yet.</summary>
    public string? TwoFactorMethod { get; set; }
    public string? TwoFactorCode { get; set; }
}

public sealed class PasswordChangeUndoRequest
{
    public string Token { get; set; } = string.Empty;
}
