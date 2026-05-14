using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SayimLink.Api.Configuration;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

public interface IJwtService
{
    (string token, DateTime expiresAt) GenerateAccessToken(User user);
    (string plaintext, string hash, DateTime expiresAt) GenerateRefreshToken(bool rememberMe);
    string HashRefreshToken(string plaintext);
    int AccessTokenSeconds { get; }

    /// <summary>Short-lived (5 min) JWT used between password verification and 2FA verification.
    /// Carries the user id + a "twofa_pending" purpose claim. Not accepted by the resource APIs.</summary>
    string GenerateTwoFactorPendingToken(User user, bool rememberMe);

    /// <summary>Returns (userId, rememberMe) on success; null on invalid/expired.</summary>
    (string userId, bool rememberMe)? ValidateTwoFactorPendingToken(string token);
}

public sealed class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;

    public JwtService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
        if (string.IsNullOrWhiteSpace(_settings.Secret) || _settings.Secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public int AccessTokenSeconds => _settings.AccessTokenMinutes * 60;

    public (string token, DateTime expiresAt) GenerateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.AdSoyad),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.AdSoyad),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Rol),
            new("firmaId", user.FirmaId ?? string.Empty),
            new("firmaIds", string.Join(',', user.FirmaIds)),
            new("magazaIds", string.Join(',', user.MagazaIds)),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string plaintext, string hash, DateTime expiresAt) GenerateRefreshToken(bool rememberMe)
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        var plaintext = Base64UrlEncoder.Encode(bytes.ToArray());

        var expiresAt = rememberMe
            ? DateTime.UtcNow.AddDays(_settings.RefreshTokenDaysRememberMe)
            : DateTime.UtcNow.AddHours(_settings.RefreshTokenHoursDefault);

        return (plaintext, HashRefreshToken(plaintext), expiresAt);
    }

    public string HashRefreshToken(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public string GenerateTwoFactorPendingToken(User user, bool rememberMe)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(5);
        // Include both `sub` and ClaimTypes.NameIdentifier so the validator can find
        // the user id regardless of inbound claim mapping behaviour.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new("purpose", "twofa_pending"),
            new("rm", rememberMe ? "1" : "0"),
        };
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string userId, bool rememberMe)? ValidateTwoFactorPendingToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = _settings.Issuer,
                ValidateIssuer = true,
                ValidAudience = _settings.Audience,
                ValidateAudience = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(10),
            }, out _);

            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != "twofa_pending") return null;
            // Default inbound mapping turns `sub` into ClaimTypes.NameIdentifier, so
            // try both lookups before declaring the token invalid.
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(sub)) return null;
            var rm = principal.FindFirst("rm")?.Value == "1";
            return (sub, rm);
        }
        catch { return null; }
    }
}
