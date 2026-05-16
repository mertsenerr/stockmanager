using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services.TwoFactor;

public interface ITotpSecretProtector
{
    /// <summary>Encrypts a base32-encoded TOTP secret for at-rest storage.</summary>
    string Protect(string plainBase32);

    /// <summary>Returns the original base32 secret. Accepts both protected ciphertext
    /// (versioned prefix "enc1:") and legacy plaintext — callers re-protect on first
    /// successful verify so the database eventually contains only ciphertext.</summary>
    string Unprotect(string stored);

    /// <summary>True if the stored value is in the legacy plaintext format and should
    /// be re-protected by the caller after a successful verification.</summary>
    bool IsLegacy(string stored);
}

/// <summary>AES-256-GCM wrapper for short symmetric secrets (TOTP seeds, etc.).
/// Output layout: <c>"enc1:" + base64(12-byte nonce || ciphertext || 16-byte tag)</c>.
/// </summary>
public sealed class TotpSecretProtector : ITotpSecretProtector
{
    private const string Prefix = "enc1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public TotpSecretProtector(IOptions<EncryptionSettings> options, IHostEnvironment env, ILogger<TotpSecretProtector> logger)
    {
        var raw = options.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "Encryption:MasterKey is not configured. Set Encryption__MasterKey env var to a base64-encoded 32-byte key. Refusing to start.");

            // Dev convenience: derive a stable ephemeral key from a fixed seed so
            // restarts don't invalidate locally-enrolled TOTP secrets, while still
            // forcing the operator to set a real key in production.
            logger.LogWarning(
                "Encryption:MasterKey not set — using insecure development key. DO NOT run this in production.");
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("sayimlink-dev-totp-key"));
            return;
        }

        try { _key = Convert.FromBase64String(raw); }
        catch (FormatException)
        {
            throw new InvalidOperationException("Encryption:MasterKey must be valid base64.");
        }
        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"Encryption:MasterKey must decode to exactly 32 bytes (got {_key.Length}).");
    }

    public bool IsLegacy(string stored) =>
        !string.IsNullOrEmpty(stored) && !stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plainBase32)
    {
        if (string.IsNullOrEmpty(plainBase32)) return string.Empty;
        var plaintext = Encoding.UTF8.GetBytes(plainBase32);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var combined = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSize + ciphertext.Length, TagSize);

        return Prefix + Convert.ToBase64String(combined);
    }

    public string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (IsLegacy(stored)) return stored; // base32 plaintext from pre-encryption rows

        var b64 = stored.AsSpan(Prefix.Length);
        var combined = Convert.FromBase64String(b64.ToString());
        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted TOTP secret is too short.");

        var nonce = combined.AsSpan(0, NonceSize);
        var tag = combined.AsSpan(combined.Length - TagSize, TagSize);
        var ciphertext = combined.AsSpan(NonceSize, combined.Length - NonceSize - TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
