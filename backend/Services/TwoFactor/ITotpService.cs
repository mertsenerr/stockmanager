using System.Security.Cryptography;
using OtpNet;
using QRCoder;

namespace SayimLink.Api.Services.TwoFactor;

public interface ITotpService
{
    /// <summary>Generates a fresh base32-encoded secret and the matching otpauth URL.</summary>
    (string secretBase32, string otpAuthUrl, string qrPngDataUri) GenerateSecret(string issuer, string accountEmail);

    /// <summary>Verifies a 6-digit TOTP code against a stored base32 secret with ±1 step tolerance.</summary>
    bool Verify(string secretBase32, string code);
}

public sealed class TotpService : ITotpService
{
    public (string secretBase32, string otpAuthUrl, string qrPngDataUri) GenerateSecret(string issuer, string accountEmail)
    {
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        var base32 = Base32Encoding.ToString(bytes).Replace("=", "");
        var label  = Uri.EscapeDataString($"{issuer}:{accountEmail}");
        var iss    = Uri.EscapeDataString(issuer);
        var url    = $"otpauth://totp/{label}?secret={base32}&issuer={iss}&algorithm=SHA1&digits=6&period=30";

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var pngBytes = new PngByteQRCode(qrData).GetGraphic(8);
        var dataUri  = "data:image/png;base64," + Convert.ToBase64String(pngBytes);
        return (base32, url, dataUri);
    }

    public bool Verify(string secretBase32, string code)
    {
        if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(secretBase32));
            return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch { return false; }
    }
}
