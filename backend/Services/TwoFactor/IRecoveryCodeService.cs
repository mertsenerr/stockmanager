using System.Security.Cryptography;
using System.Text;

namespace SayimLink.Api.Services.TwoFactor;

public interface IRecoveryCodeService
{
    /// <summary>Generates 10 plaintext codes (XXXX-XXXX-XXXX) plus their sha256 hashes.</summary>
    (List<string> plaintext, List<string> hashes) Generate(int count = 10);

    /// <summary>Tries to consume one matching hash from the given list, returning the new
    /// list (without the matched code) on success.</summary>
    bool TryConsume(IList<string> existingHashes, string suppliedCode, out List<string> remaining);
}

public sealed class RecoveryCodeService : IRecoveryCodeService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // unambiguous

    public (List<string> plaintext, List<string> hashes) Generate(int count = 10)
    {
        var plain = new List<string>(count);
        var hashes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var code = GenerateCode();
            plain.Add(code);
            hashes.Add(Hash(code));
        }
        return (plain, hashes);
    }

    public bool TryConsume(IList<string> existingHashes, string suppliedCode, out List<string> remaining)
    {
        var normalized = NormalizeForCompare(suppliedCode);
        var hash = Hash(normalized);
        if (existingHashes.Contains(hash))
        {
            remaining = existingHashes.Where(h => h != hash).ToList();
            return true;
        }
        remaining = existingHashes.ToList();
        return false;
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

    private static string Hash(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
