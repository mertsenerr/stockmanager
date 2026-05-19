using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = Roles.Kullanici;

    // Yeni model: birincil firma. Eski FirmaIds geri uyumluluk için tutuluyor.
    [BsonRepresentation(BsonType.ObjectId)]
    public string? FirmaId { get; set; }

    public List<string> FirmaIds { get; set; } = [];
    public List<string> MagazaIds { get; set; } = [];

    public bool AktifMi { get; set; } = true;

    /// <summary>
    /// Profil fotoğrafı, data URI olarak inline saklanır (data:image/...;base64,...).
    /// Frontend kırpıp 240px kareye düşürüyor, ~50KB sınırında tutuyoruz —
    /// User doc'unu şişirmemek için. Null = avatar yok, initials gösterilsin.
    /// </summary>
    public string? AvatarDataUri { get; set; }


    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? SonGirisTarihi { get; set; }

    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    // ─── Password-change undo (defense-in-depth) ────────────────────────────
    // After a successful password change we keep the previous hash + a
    // single-use undo token for ~30 min, so the original owner can revert if
    // an attacker did it. Cleared on first use or expiry.
    public string? PasswordChangeUndoTokenHash { get; set; }
    public DateTime? PasswordChangeUndoExpiresAt { get; set; }
    public string? PasswordChangeUndoPreviousHash { get; set; }

    // Defaults to true so docs written before the H-3 field existed deserialize as
    // verified; the register flow explicitly sets false for new sign-ups, and the
    // Phase2 migration sets true on every existing record as a belt-and-suspenders.
    public bool IsEmailVerified { get; set; } = true;
    public string? EmailVerificationTokenHash { get; set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; set; }

    // ─── 2FA fields ────────────────────────────────────────────────────────
    // TOTP (authenticator app). Secret stored base32-encoded.
    public bool TotpEnabled { get; set; }
    public string? TotpSecret { get; set; }

    // Email OTP. Code is hashed (sha256) with TTL during a verification window.
    public bool EmailOtpEnabled { get; set; }
    public string? EmailOtpCodeHash { get; set; }
    public DateTime? EmailOtpExpiresAt { get; set; }
    // Cooldown / cap state for /2fa/email/send. The pending-token caller already
    // proved the password but should not be able to spam the user's inbox or burn
    // Resend quota by repeatedly requesting new codes.
    public DateTime? EmailOtpLastSentAt { get; set; }
    public DateTime? EmailOtpDayWindowStart { get; set; }
    public int EmailOtpDayCount { get; set; }

    // WebAuthn / passkey credentials. Each item stores a CBOR-encoded credential
    // descriptor produced by Fido2NetLib's StoredCredential.
    public List<WebAuthnCredential> WebAuthnCredentials { get; set; } = [];

    // Account-wide single-use recovery codes (works for any 2FA method).
    // Each is a sha256 hash of the plaintext shown to the user once.
    public List<string> RecoveryCodeHashes { get; set; } = [];

    // ─── 2FA brute-force throttling ────────────────────────────────────────
    // Per-user attempt counter on the /2fa/verify path. The IP-based rate limit
    // doesn't help against rotating-IP attackers who already know the password
    // and are guessing the second factor; this counter does.
    public int TwoFactorFailedAttempts { get; set; }
    public DateTime? TwoFactorLockedUntil { get; set; }

    // ─── Login brute-force throttling ──────────────────────────────────────
    // Same idea on the password verification step. Distributed credential-
    // stuffing rotates IPs so the IP rate limiter alone is bypassable; a
    // per-account counter caps the total guess budget regardless of source.
    public int LoginFailedAttempts { get; set; }
    public DateTime? LoginLockedUntil { get; set; }

    // ─── JWT access-token revocation cut-off ───────────────────────────────
    // JWT access tokens are stateless — we can't "delete" them, only outwait
    // their exp. When an admin pacifies / deletes a user, or the user changes
    // their password, we bump this timestamp; the JwtBearer OnTokenValidated
    // hook then rejects any access token whose `iat` is older.
    public DateTime? TokenInvalidatedAt { get; set; }
}

public sealed class WebAuthnCredential
{
    public string CredentialId { get; set; } = string.Empty;          // base64url
    public string PublicKeyCose { get; set; } = string.Empty;         // base64
    public string UserHandle { get; set; } = string.Empty;            // base64
    public uint SignatureCounter { get; set; }
    public string CredType { get; set; } = "public-key";
    public string? AaGuid { get; set; }
    public string? Nickname { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class TwoFactorMethods
{
    public const string Totp     = "totp";
    public const string Email    = "email";
    public const string WebAuthn = "webauthn";
    public const string Recovery = "recovery";
}
