using Fido2NetLib;

namespace SayimLink.Api.Dtos.Auth;

/// <summary>Returned by /login when the user has 2FA enabled. Frontend swaps to the
/// 2FA verification screen and posts the chosen second-factor proof back.</summary>
public sealed class TwoFactorRequiredResponse
{
    public bool RequiresTwoFactor { get; set; } = true;
    public string PendingToken { get; set; } = string.Empty;
    public List<string> AvailableMethods { get; set; } = [];
}

public sealed class TwoFactorStatusDto
{
    public bool TotpEnabled { get; set; }
    public bool EmailOtpEnabled { get; set; }
    public bool WebAuthnEnabled { get; set; }
    public int  WebAuthnCredentialCount { get; set; }
    public int  RecoveryCodesRemaining { get; set; }
}

public sealed class TotpSetupResponse
{
    public string Secret { get; set; } = string.Empty;
    public string OtpAuthUrl { get; set; } = string.Empty;
    public string QrPngDataUri { get; set; } = string.Empty;
}

public sealed class TotpEnableRequest
{
    public string Code { get; set; } = string.Empty;
}

public sealed class EmailOtpVerifyRequest
{
    public string Code { get; set; } = string.Empty;
}

public sealed class TwoFactorVerifyRequest
{
    /// <summary>Pending token issued by /login.</summary>
    public string PendingToken { get; set; } = string.Empty;

    /// <summary>One of: "totp" | "email" | "webauthn" | "recovery".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>For totp/email/recovery — the code/typing input.</summary>
    public string? Code { get; set; }

    /// <summary>For webauthn — the assertion response payload (JSON-serialized).</summary>
    public AuthenticatorAssertionRawResponse? AssertionResponse { get; set; }
}

public sealed class WebAuthnRegisterCompleteRequest
{
    public AuthenticatorAttestationRawResponse Response { get; set; } = null!;
    public string? Nickname { get; set; }
}

public sealed class RecoveryCodesResponse
{
    public List<string> Codes { get; set; } = [];
}

public sealed class WebAuthnCredentialDto
{
    public string Id { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PendingTokenRequest
{
    public string PendingToken { get; set; } = string.Empty;
}
