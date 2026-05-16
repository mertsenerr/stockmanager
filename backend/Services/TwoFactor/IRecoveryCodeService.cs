using System.Security.Cryptography;
using System.Text;

namespace SayimLink.Api.Services.TwoFactor;

public interface IRecoveryCodeService
{
    /// <summary>Generates 10 plaintext codes (XXXX-XXXX-XXXX) plus their BCrypt hashes.</summary>
    (List<string> plaintext, List<string> hashes) Generate(int count = 10);

    /// <summary>Tries to consume one matching hash from the given list, returning the new
    /// list (without the matched code) on success.</summary>
    bool TryConsume(IList<string> existingHashes, string suppliedCode, out List<string> remaining);

    /// <summary>Same match logic as <see cref="TryConsume"/> but returns the matched hash
    /// instead of rewriting the list — for callers that want to do an atomic <c>$pull</c>
    /// at the database layer instead of full-document replace.</summary>
    bool TryMatch(IList<string> existingHashes, string suppliedCode, out string? matchedHash);
}

public sealed class RecoveryCodeService : IRecoveryCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // unambiguous
    private const int BcryptWorkFactor = 10;

    public (List<string> plaintext, List<string> hashes) Generate(int count = 10)
    {
        var plain = new List<string>(count);
        var hashes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var code = GenerateCode();
            plain.Add(code);
            hashes.Add(BCrypt.Net.BCrypt.HashPassword(code, BcryptWorkFactor));
        }
        return (plain, hashes);
    }

    public bool TryConsume(IList<string> existingHashes, string suppliedCode, out List<string> remaining)
    {
        if (TryMatch(existingHashes, suppliedCode, out var matched) && matched is not null)
        {
            remaining = existingHashes.Where(h => h != matched).ToList();
            return true;
        }
        remaining = existingHashes.ToList();
        return false;
    }

    public bool TryMatch(IList<string> existingHashes, string suppliedCode, out string? matchedHash)
    {
        var normalized = NormalizeForCompare(suppliedCode);
        var legacyHash = LegacySha256Hex(normalized);

        // Each stored entry is either a BCrypt hash (new format, starts with $2)
        // or a hex sha256 (legacy plaintext-hashed). Walk every entry so we can
        // identify the matching one regardless of format.
        for (var i = 0; i < existingHashes.Count; i++)
        {
            var stored = existingHashes[i];
            var matched = stored.StartsWith("$2", StringComparison.Ordinal)
                ? SafeBcryptVerify(normalized, stored)
                : string.Equals(stored, legacyHash, StringComparison.OrdinalIgnoreCase);
            if (matched)
            {
                matchedHash = stored;
                return true;
            }
        }

        matchedHash = null;
        return false;
    }

    private static bool SafeBcryptVerify(string plain, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(plain, hash); }
        catch { return false; }
    }

    private static string GenerateCode()
    {
        // 12 chars total, formatted XXXX-XXXX-XXXX for readability.
        Span<byte> buf = stackalloc byte[12];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(14);
        for (var i = 0; i < buf.Length; i++)
        {
            if (i == 4 || i == 8) sb.Append('-');
            sb.Append(Alphabet[buf[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    private static string NormalizeForCompare(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        }
        // Re-format to XXXX-XXXX-XXXX so hash matches.
        var raw = sb.ToString();
        if (raw.Length != 12) return raw;
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    }

    private static string LegacySha256Hex(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
