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
}
