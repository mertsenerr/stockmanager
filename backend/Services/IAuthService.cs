using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;
using SayimLink.Api.Dtos.Auth;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshTokenPlaintext, string? ip, string? userAgent, CancellationToken ct = default);
    Task LogoutAsync(string refreshTokenPlaintext, CancellationToken ct = default);
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);
    Task<bool> ResetPasswordAsync(string tokenPlaintext, string newPassword, CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(string userId, CancellationToken ct = default);
    Task<RegisterResult> RegisterSayimBaskaniAsync(RegisterSayimBaskaniRequest request, CancellationToken ct = default);
    Task<RegisterResult> RegisterKullaniciAsync(RegisterKullaniciRequest request, CancellationToken ct = default);
    Task<bool> VerifyEmailAsync(string tokenPlaintext, CancellationToken ct = default);
    Task RequestEmailVerificationAsync(string email, CancellationToken ct = default);
}

public static class AuthFailureCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailNotVerified  = "EMAIL_NOT_VERIFIED";
    public const string NotApproved       = "NOT_APPROVED";
    public const string RefreshInvalid    = "REFRESH_INVALID";
}

public sealed record RegisterResult(
    bool Success,
    string? FailureReason,
    UserDto? User);

public sealed record AuthResult(
    bool Success,
    string? FailureReason,
    string? FailureCode,
    AuthResponse? Response,
    string? RefreshTokenPlaintext,
    DateTime? RefreshTokenExpiresAt,
    bool RememberMe);

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IFirmaRepository _firmalar;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;
    private readonly IEmailService _email;
    private readonly ResendSettings _resendSettings;
    private readonly ILogger<AuthService> _logger;

    private static readonly TimeSpan PasswordResetTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EmailVerificationTtl = TimeSpan.FromHours(24);

    public AuthService(
        IUserRepository users,
        IFirmaRepository firmalar,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IJwtService jwt,
        IEmailService email,
        IOptions<ResendSettings> resendSettings,
        ILogger<AuthService> logger)
    {
        _users = users;
        _firmalar = firmalar;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _jwt = jwt;
        _email = email;
        _resendSettings = resendSettings.Value;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? ip,
        string? userAgent,
        CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(request.Email, ct);
        if (user is null || !user.AktifMi || !_hasher.Verify(request.Password, user.PasswordHash))
            return new AuthResult(false, "Email veya parola hatalı.", AuthFailureCodes.InvalidCredentials, null, null, null, false);

        if (!user.IsEmailVerified)
            return new AuthResult(false, "Lütfen önce e-posta adresinizi doğrulayın.", AuthFailureCodes.EmailNotVerified, null, null, null, false);

        if (!user.Onayli)
            return new AuthResult(false, "Sayım Başkanınızın onayını bekliyorsunuz.", AuthFailureCodes.NotApproved, null, null, null, false);

        user.SonGirisTarihi = DateTime.UtcNow;
        await _users.ReplaceAsync(user, ct);

        return await IssueTokensAsync(user, request.RememberMe, ip, userAgent, ct);
    }

    public async Task<RegisterResult> RegisterSayimBaskaniAsync(
        RegisterSayimBaskaniRequest request,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, ct) is not null)
            return new RegisterResult(false, "Bu e-posta zaten kayıtlı.", null);

        var kisaltma = request.FirmaKisaltmasi.Trim().ToUpperInvariant();
        var ad = request.FirmaAdi.Trim();

        // Phase 2.5: firma name + kisaltma uniqueness no longer enforced. Each SayimBaskani
        // register creates their own personal Firma — duplicates of name/kisaltma are
        // expected and allowed. We surface them in logs to make the registration pattern
        // visible without blocking the user.
        if (await _firmalar.AdExistsAsync(ad, null, ct))
            _logger.LogWarning(
                "SayimBaskani register: firma name '{Ad}' already in use — creating duplicate per personal tenancy", ad);
        if (await _firmalar.KisaltmaExistsAsync(kisaltma, null, ct))
            _logger.LogWarning(
                "SayimBaskani register: kisaltma '{Kisaltma}' already in use — creating duplicate per personal tenancy", kisaltma);

        var firma = new Firma
        {
            Ad = ad,
            Kisaltma = kisaltma,
            Tip = FirmaTipleri.Diger,
            AktifMi = true,
            OrganizasyonMu = true, // Sayım Başkanı'nın kendi sayım organizasyonu
        };
        await _firmalar.InsertAsync(firma, ct);

        var user = new User
        {
            Email = email,
            AdSoyad = request.AdSoyad.Trim(),
            Rol = Roles.SayimBaskani,
            PasswordHash = _hasher.Hash(request.Password),
            FirmaId = firma.Id,
            FirmaIds = [firma.Id],
            AktifMi = true,
            Onayli = true,
        };
        // H-3: register flow now requires email verification — set token, send mail.
        user.IsEmailVerified = false;
        var rawToken = GenerateOpaqueToken();
        user.EmailVerificationTokenHash = _jwt.HashRefreshToken(rawToken);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(EmailVerificationTtl);

        firma.OlusturanKullaniciId = user.Id;
        await _firmalar.ReplaceAsync(firma, ct);
        await _users.InsertAsync(user, ct);

        await SendVerificationEmailSafelyAsync(user, rawToken, ct);

        _logger.LogInformation(
            "New SayimBaskani registered: {Email} for firma {FirmaAd} ({Kisaltma})",
            email, ad, kisaltma);

        return new RegisterResult(true, null, ToDto(user));
    }

    public async Task<RegisterResult> RegisterKullaniciAsync(
        RegisterKullaniciRequest request,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, ct) is not null)
            return new RegisterResult(false, "Bu e-posta zaten kayıtlı.", null);

        var kisaltma = request.FirmaKisaltmasi.Trim().ToUpperInvariant();
        var firma = await _firmalar.FindByKisaltmaAsync(kisaltma, ct);
        if (firma is null)
            return new RegisterResult(false, "Firma kısaltması bulunamadı. Sayım Başkanından doğru anahtarı isteyin.", null);

        var rawToken = GenerateOpaqueToken();
        var user = new User
        {
            Email = email,
            AdSoyad = request.AdSoyad.Trim(),
            Rol = Roles.Kullanici,
            PasswordHash = _hasher.Hash(request.Password),
            FirmaId = firma.Id,
            FirmaIds = [firma.Id],
            AktifMi = true,
            Onayli = false, // Sayım Başkanı onayı bekleyecek
            IsEmailVerified = false,
            EmailVerificationTokenHash = _jwt.HashRefreshToken(rawToken),
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(EmailVerificationTtl),
        };
        await _users.InsertAsync(user, ct);

        await SendVerificationEmailSafelyAsync(user, rawToken, ct);

        _logger.LogInformation(
            "New Kullanici registered (pending approval): {Email} for firma {Kisaltma}",
            email, kisaltma);

        return new RegisterResult(true, null, ToDto(user));
    }

    public async Task<AuthResult> RefreshAsync(
        string refreshTokenPlaintext,
        string? ip,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlaintext))
            return new AuthResult(false, "Refresh token bulunamadı.", AuthFailureCodes.RefreshInvalid, null, null, null, false);

        var hash = _jwt.HashRefreshToken(refreshTokenPlaintext);
        var existing = await _refreshTokens.FindByHashAsync(hash, ct);

        if (existing is null)
            return new AuthResult(false, "Refresh token geçersiz.", AuthFailureCodes.RefreshInvalid, null, null, null, false);

        if (!existing.IsActive)
        {
            // Token reuse detected → revoke whole family.
            if (existing.RevokedAt is not null)
            {
                _logger.LogWarning("Refresh token reuse detected for user {UserId}", existing.UserId);
                await _refreshTokens.RevokeAllForUserAsync(existing.UserId, "reuse_detected", ct);
            }
            return new AuthResult(false, "Refresh token süresi dolmuş.", AuthFailureCodes.RefreshInvalid, null, null, null, false);
        }

        var user = await _users.FindByIdAsync(existing.UserId, ct);
        if (user is null || !user.AktifMi)
            return new AuthResult(false, "Kullanıcı bulunamadı.", AuthFailureCodes.RefreshInvalid, null, null, null, false);

        // Rotate.
        var rememberMe = (existing.ExpiresAt - existing.CreatedAt).TotalDays > 1;
        var newResult = await IssueTokensAsync(user, rememberMe, ip, userAgent, ct);

        existing.RevokedAt = DateTime.UtcNow;
        existing.RevokedReason = "rotated";
        existing.ReplacedByTokenId = await GetLatestTokenIdAsync(user.Id, ct);
        await _refreshTokens.ReplaceAsync(existing, ct);

        return newResult;
    }

    public async Task LogoutAsync(string refreshTokenPlaintext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlaintext)) return;

        var hash = _jwt.HashRefreshToken(refreshTokenPlaintext);
        var existing = await _refreshTokens.FindByHashAsync(hash, ct);
        if (existing is null || existing.RevokedAt is not null) return;

        existing.RevokedAt = DateTime.UtcNow;
        existing.RevokedReason = "logout";
        await _refreshTokens.ReplaceAsync(existing, ct);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email, ct);
        if (user is null || !user.AktifMi)
        {
            // Do not reveal account existence.
            _logger.LogInformation("Password reset requested for unknown email {Email}", email);
            return;
        }

        var rawToken = GenerateOpaqueToken();
        user.PasswordResetTokenHash = _jwt.HashRefreshToken(rawToken);
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.Add(PasswordResetTtl);
        await _users.ReplaceAsync(user, ct);

        var resetUrl = _resendSettings.PasswordResetUrlTemplate.Replace("{token}", rawToken);
        await _email.SendPasswordResetAsync(user.Email, user.AdSoyad, resetUrl, ct);
    }

    public async Task<bool> VerifyEmailAsync(string tokenPlaintext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenPlaintext)) return false;

        var hash = _jwt.HashRefreshToken(tokenPlaintext);
        var user = await _users.FindByEmailVerificationHashAsync(hash, ct);
        if (user is null
            || user.EmailVerificationTokenExpiresAt is null
            || user.EmailVerificationTokenExpiresAt < DateTime.UtcNow)
            return false;

        user.IsEmailVerified = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAt = null;
        await _users.ReplaceAsync(user, ct);
        return true;
    }

    public async Task RequestEmailVerificationAsync(string email, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(email, ct);
        if (user is null || !user.AktifMi || user.IsEmailVerified)
        {
            // Do not reveal account existence or verification state.
            return;
        }

        var rawToken = GenerateOpaqueToken();
        user.EmailVerificationTokenHash = _jwt.HashRefreshToken(rawToken);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(EmailVerificationTtl);
        await _users.ReplaceAsync(user, ct);

        await SendVerificationEmailSafelyAsync(user, rawToken, ct);
    }

    private async Task SendVerificationEmailSafelyAsync(User user, string rawToken, CancellationToken ct)
    {
        var template = _resendSettings.EmailVerificationUrlTemplate;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{token}"))
        {
            _logger.LogWarning(
                "EmailVerificationUrlTemplate not configured — skipping send for {Email}",
                user.Email);
            return;
        }

        var verifyUrl = template.Replace("{token}", rawToken);
        await _email.SendEmailVerificationAsync(user.Email, user.AdSoyad, verifyUrl, ct);
    }

    public async Task<bool> ResetPasswordAsync(string tokenPlaintext, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenPlaintext)) return false;

        var hash = _jwt.HashRefreshToken(tokenPlaintext);
        var user = await _users.FindByPasswordResetHashAsync(hash, ct);
        if (user is null || user.PasswordResetTokenExpiresAt is null
            || user.PasswordResetTokenExpiresAt < DateTime.UtcNow)
            return false;

        user.PasswordHash = _hasher.Hash(newPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        await _users.ReplaceAsync(user, ct);

        await _refreshTokens.RevokeAllForUserAsync(user.Id, "password_reset", ct);
        return true;
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null || !user.AktifMi) return null;
        return await ToDtoEnrichedAsync(user, ct);
    }

    private async Task<AuthResult> IssueTokensAsync(
        User user,
        bool rememberMe,
        string? ip,
        string? userAgent,
        CancellationToken ct)
    {
        var (access, _) = _jwt.GenerateAccessToken(user);
        var (refreshPlain, refreshHash, refreshExp) = _jwt.GenerateRefreshToken(rememberMe);

        await _refreshTokens.InsertAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExp,
            CreatedByIp = ip,
            UserAgent = userAgent,
        }, ct);

        return new AuthResult(
            Success: true,
            FailureReason: null,
            FailureCode: null,
            Response: new AuthResponse
            {
                AccessToken = access,
                ExpiresInSeconds = _jwt.AccessTokenSeconds,
                User = await ToDtoEnrichedAsync(user, ct),
            },
            RefreshTokenPlaintext: refreshPlain,
            RefreshTokenExpiresAt: refreshExp,
            RememberMe: rememberMe);
    }

    private async Task<UserDto> ToDtoEnrichedAsync(User user, CancellationToken ct)
    {
        var dto = ToDto(user);
        if (!string.IsNullOrEmpty(user.FirmaId))
        {
            var firma = await _firmalar.FindByIdAsync(user.FirmaId, ct);
            if (firma is not null)
            {
                dto.FirmaAdi = firma.Ad;
                dto.FirmaKisaltmasi = firma.Kisaltma;
            }
        }
        return dto;
    }

    private async Task<string?> GetLatestTokenIdAsync(string userId, CancellationToken ct)
    {
        // Tracking the replacement chain is best-effort; if we cannot resolve, leave null.
        await Task.CompletedTask;
        return null;
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        AdSoyad = user.AdSoyad,
        Rol = user.Rol,
        FirmaId = user.FirmaId,
        FirmaIds = user.FirmaIds,
        MagazaIds = user.MagazaIds,
        Onayli = user.Onayli,
    };

    private static string GenerateOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
