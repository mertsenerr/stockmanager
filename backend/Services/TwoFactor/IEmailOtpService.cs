using System.Security.Cryptography;
using System.Text;

namespace SayimLink.Api.Services.TwoFactor;

public interface IEmailOtpService
{
    /// <summary>Generates a 6-digit numeric code, returns plaintext + sha256 hash + expiry.</summary>
    (string plaintext, string hash, DateTime expiresAt) Generate(TimeSpan? ttl = null);

    /// <summary>Returns true if supplied code matches the stored hash and isn't expired.</summary>
    bool Verify(string? storedHash, DateTime? expiresAt, string suppliedCode);
}

public sealed class EmailOtpService : IEmailOtpService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public (string plaintext, string hash, DateTime expiresAt) Generate(TimeSpan? ttl = null)
    {
        // 6-digit zero-padded code (000000 to 999999).
        Span<byte> buf = stackalloc byte[4];
        RandomNumberGenerator.Fill(buf);
        var n = BitConverter.ToUInt32(buf);
        var code = (n % 1_000_000).ToString("D6");
        return (code, Hash(code), DateTime.UtcNow.Add(ttl ?? DefaultTtl));
    }

    public bool Verify(string? storedHash, DateTime? expiresAt, string suppliedCode)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || expiresAt is null) return false;
        if (expiresAt < DateTime.UtcNow) return false;
        return string.Equals(storedHash, Hash(suppliedCode.Trim()), StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
