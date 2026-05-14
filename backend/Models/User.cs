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

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? SonGirisTarihi { get; set; }

    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

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

    // WebAuthn / passkey credentials. Each item stores a CBOR-encoded credential
    // descriptor produced by Fido2NetLib's StoredCredential.
    public List<WebAuthnCredential> WebAuthnCredentials { get; set; } = [];

    // Account-wide single-use recovery codes (works for any 2FA method).
    // Each is a sha256 hash of the plaintext shown to the user once.
    public List<string> RecoveryCodeHashes { get; set; } = [];
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
