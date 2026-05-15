using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;
using SayimLink.Api.Dtos.Auth;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services.TwoFactor;

namespace SayimLink.Api.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, string? ip, string? userAgent, string? deviceId, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string refreshTokenPlaintext, string? ip, string? userAgent, string? deviceId, CancellationToken ct = default);
    Task LogoutAsync(string refreshTokenPlaintext, CancellationToken ct = default);
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);
    Task<bool> ResetPasswordAsync(string tokenPlaintext, string newPassword, CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(string userId, CancellationToken ct = default);
    Task<UserDto?> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);
    Task<ChangePasswordResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, string? currentRefreshToken, CancellationToken ct = default);
    Task<bool> UndoPasswordChangeAsync(string token, CancellationToken ct = default);
    Task<long> RevokeOtherSessionsAsync(string userId, string? currentRefreshToken, CancellationToken ct = default);

    Task<IReadOnlyList<ActiveSessionDto>> ListActiveSessionsAsync(string userId, string? currentRefreshToken, CancellationToken ct = default);
    Task<bool> RevokeSessionAsync(string userId, string sessionId, string? currentRefreshToken, CancellationToken ct = default);

    /// <summary>Issues real tokens after a successful 2FA verification step.</summary>
    Task<AuthResult> CompleteTwoFactorLoginAsync(string userId, bool rememberMe, string? ip, string? userAgent, string? deviceId, CancellationToken ct = default);

    Task<User?> GetUserAsync(string userId, CancellationToken ct = default);
    Task ReplaceUserAsync(User user, CancellationToken ct = default);
    Task<RegisterResult> RegisterSayimBaskaniAsync(RegisterSayimBaskaniRequest request, CancellationToken ct = default);
    Task<RegisterResult> RegisterKullaniciAsync(RegisterKullaniciRequest request, CancellationToken ct = default);
    Task<bool> VerifyEmailAsync(string tokenPlaintext, CancellationToken ct = default);
    Task RequestEmailVerificationAsync(string email, CancellationToken ct = default);
}

public static class AuthFailureCodes
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailNotVerified  = "EMAIL_NOT_VERIFIED";
    public const string RefreshInvalid    = "REFRESH_INVALID";
}

public sealed record RegisterResult(
    bool Success,
    string? FailureReason,
    UserDto? User);

public sealed record ChangePasswordResult(
    bool Success,
    string? FailureReason);

public sealed record AuthResult(
    bool Success,
    string? FailureReason,
    string? FailureCode,
    AuthResponse? Response,
    string? RefreshTokenPlaintext,
    DateTime? RefreshTokenExpiresAt,
    bool RememberMe,
    // When a user has 2FA enabled, LoginAsync returns Success=true with these set instead
    // of issuing a refresh token. The caller must complete the second factor.
    string? TwoFactorPendingToken = null,
    IReadOnlyList<string>? TwoFactorAvailableMethods = null,
    // The DeviceId that was actually stamped on the refresh token. May differ
    // from the inbound cookie value if the service had to UA-match-fallback
    // (cookie missing) or carry forward a legacy row's existing DeviceId.
    // Controller should write this back to the slk_did cookie to keep the
    // browser in sync with the server's view of "which device am I".
    string? DeviceId = null);

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
    private readonly ITotpService _totp;
    private readonly IEmailOtpService _emailOtp;
    private readonly IRecoveryCodeService _recovery;

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
        ILogger<AuthService> logger,
        ITotpService totp,
        IEmailOtpService emailOtp,
        IRecoveryCodeService recovery)
    {
        _users = users;
        _firmalar = firmalar;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _jwt = jwt;
        _email = email;
        _resendSettings = resendSettings.Value;
        _logger = logger;
        _totp = totp;
        _emailOtp = emailOtp;
        _recovery = recovery;
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? ip,
        string? userAgent,
        string? deviceId,
        CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(request.Email, ct);
        if (user is null || !user.AktifMi || !_hasher.Verify(request.Password, user.PasswordHash))
            return new AuthResult(false, "Email veya parola hatalı.", AuthFailureCodes.InvalidCredentials, null, null, null, false);

        if (!user.IsEmailVerified)
            return new AuthResult(false, "Lütfen önce e-posta adresinizi doğrulayın.", AuthFailureCodes.EmailNotVerified, null, null, null, false);

        // 2FA gate: if any second factor is enabled, do NOT issue refresh tokens yet —
        // hand back a short-lived "pending" JWT and let the caller finish via /2fa/verify.
        var methods = new List<string>();
        if (user.TotpEnabled)                          methods.Add("totp");
        if (user.WebAuthnCredentials.Count > 0)        methods.Add("webauthn");
        if (user.EmailOtpEnabled)                      methods.Add("email");
        if (user.RecoveryCodeHashes.Count > 0)         methods.Add("recovery");

        if (methods.Count > 0 && (user.TotpEnabled || user.EmailOtpEnabled || user.WebAuthnCredentials.Count > 0))
        {
            var pending = _jwt.GenerateTwoFactorPendingToken(user, request.RememberMe);
            return new AuthResult(true, null, null, null, null, null, request.RememberMe, pending, methods);
        }

        user.SonGirisTarihi = DateTime.UtcNow;
        await _users.ReplaceAsync(user, ct);
        return await IssueTokensAsync(user, request.RememberMe, ip, userAgent, deviceId, ct);
    }

    public async Task<AuthResult> CompleteTwoFactorLoginAsync(
        string userId, bool rememberMe, string? ip, string? userAgent, string? deviceId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null || !user.AktifMi)
            return new AuthResult(false, "Kullanıcı bulunamadı.", AuthFailureCodes.InvalidCredentials, null, null, null, false);
        user.SonGirisTarihi = DateTime.UtcNow;
        await _users.ReplaceAsync(user, ct);
        return await IssueTokensAsync(user, rememberMe, ip, userAgent, deviceId, ct);
    }

    public Task<User?> GetUserAsync(string userId, CancellationToken ct = default) =>
        _users.FindByIdAsync(userId, ct);

    public Task ReplaceUserAsync(User user, CancellationToken ct = default) =>
        _users.ReplaceAsync(user, ct);

    public async Task<RegisterResult> RegisterSayimBaskaniAsync(
        RegisterSayimBaskaniRequest request,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, ct) is not null)
            return new RegisterResult(false, "Bu e-posta zaten kayıtlı.", null);

        var ad = request.FirmaAdi.Trim();
        if (await _firmalar.AdExistsAsync(ad, null, ct))
            _logger.LogWarning(
                "SayimBaskani register: firma name '{Ad}' already in use — creating duplicate per personal tenancy", ad);

        var firma = new Firma
        {
            Ad = ad,
            Tip = FirmaTipleri.Diger,
            AktifMi = true,
            OrganizasyonMu = true,
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
        };
        user.IsEmailVerified = false;
        var rawToken = GenerateOpaqueToken();
        user.EmailVerificationTokenHash = _jwt.HashRefreshToken(rawToken);
        user.EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(EmailVerificationTtl);

        firma.OlusturanKullaniciId = user.Id;
        await _firmalar.ReplaceAsync(firma, ct);
        await _users.InsertAsync(user, ct);

        await SendVerificationEmailSafelyAsync(user, rawToken, ct);

        _logger.LogInformation(
            "New SayimBaskani registered: {Email} for firma {FirmaAd}",
            email, ad);

        return new RegisterResult(true, null, ToDto(user));
    }

    public async Task<RegisterResult> RegisterKullaniciAsync(
        RegisterKullaniciRequest request,
        CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, ct) is not null)
            return new RegisterResult(false, "Bu e-posta zaten kayıtlı.", null);

        var rawToken = GenerateOpaqueToken();
        var user = new User
        {
            Email = email,
            AdSoyad = request.AdSoyad.Trim(),
            Rol = Roles.Kullanici,
            PasswordHash = _hasher.Hash(request.Password),
            AktifMi = true,
            IsEmailVerified = false,
            EmailVerificationTokenHash = _jwt.HashRefreshToken(rawToken),
            EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(EmailVerificationTtl),
        };
        await _users.InsertAsync(user, ct);

        await SendVerificationEmailSafelyAsync(user, rawToken, ct);

        _logger.LogInformation("New Kullanici registered: {Email}", email);

        return new RegisterResult(true, null, ToDto(user));
    }

    public async Task<AuthResult> RefreshAsync(
        string refreshTokenPlaintext,
        string? ip,
        string? userAgent,
        string? deviceId,
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

        // Carry the device identity forward across rotations: if the existing row
        // already has one, keep it; otherwise adopt whatever the caller supplied.
        // This lets pre-DeviceId rows pick up an id on first rotation.
        var effectiveDeviceId = existing.DeviceId ?? deviceId;

        // Rotate.
        var rememberMe = (existing.ExpiresAt - existing.CreatedAt).TotalDays > 1;
        var newResult = await IssueTokensAsync(user, rememberMe, ip, userAgent, effectiveDeviceId, ct);

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

    public async Task<UserDto?> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null || !user.AktifMi) return null;
        user.AdSoyad = request.AdSoyad.Trim();
        await _users.ReplaceAsync(user, ct);
        return await ToDtoEnrichedAsync(user, ct);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        string userId, ChangePasswordRequest request,
        string? currentRefreshToken, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null || !user.AktifMi)
            return new ChangePasswordResult(false, "Kullanıcı bulunamadı.");

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            return new ChangePasswordResult(false, "Mevcut parola hatalı.");

        // Step-up: if any 2FA method is enabled, require a fresh second factor
        // for this sensitive operation even though the user is already signed in.
        var has2FA = user.TotpEnabled || user.EmailOtpEnabled || user.WebAuthnCredentials.Count > 0;
        if (has2FA)
        {
            var ok = await VerifyStepUpAsync(user, request.TwoFactorMethod, request.TwoFactorCode, ct);
            if (!ok) return new ChangePasswordResult(false, "İkinci faktör doğrulaması başarısız. Lütfen geçerli bir kod gir.");
        }

        // Stash the previous hash + a single-use undo token before overwriting.
        var previousHash = user.PasswordHash;
        var undoPlain = GenerateOpaqueToken();
        user.PasswordChangeUndoTokenHash = _jwt.HashRefreshToken(undoPlain);
        user.PasswordChangeUndoExpiresAt = DateTime.UtcNow.AddMinutes(30);
        user.PasswordChangeUndoPreviousHash = previousHash;

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        await _users.ReplaceAsync(user, ct);

        // Security best-practice: invalidate every OTHER session so the password
        // change kicks intruders out, but the current session keeps working.
        if (!string.IsNullOrWhiteSpace(currentRefreshToken))
        {
            var keepHash = _jwt.HashRefreshToken(currentRefreshToken);
            await _refreshTokens.RevokeAllForUserExceptAsync(user.Id, keepHash, "password_changed", ct);
        }
        else
        {
            await _refreshTokens.RevokeAllForUserAsync(user.Id, "password_changed", ct);
        }

        // Notify the account owner so an attacker who already controls the session
        // can't quietly change the password without the real user finding out.
        var template = _resendSettings.PasswordChangeUndoUrlTemplate;
        if (!string.IsNullOrWhiteSpace(template) && template.Contains("{token}"))
        {
            var undoUrl = template.Replace("{token}", undoPlain);
            try { await _email.SendPasswordChangedAsync(user.Email, user.AdSoyad, undoUrl, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send password-changed notice to {Email}", user.Email); }
        }
        else
        {
            _logger.LogWarning(
                "PasswordChangeUndoUrlTemplate not configured — undo email skipped. Token for {Email}: {Token}",
                user.Email, undoPlain);
        }

        return new ChangePasswordResult(true, null);
    }

    public async Task<bool> UndoPasswordChangeAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var hash = _jwt.HashRefreshToken(token);

        // Find the user by undo token hash. We don't have a dedicated index for this,
        // so this is a single-doc lookup via the existing collection.
        // Mongo: scan by indexed PasswordChangeUndoTokenHash if present, else linear.
        // For low volume that's fine; add an index later if it grows.
        var user = await FindUserByUndoHashAsync(hash, ct);
        if (user is null) return false;
        if (user.PasswordChangeUndoExpiresAt is null || user.PasswordChangeUndoExpiresAt < DateTime.UtcNow) return false;
        if (string.IsNullOrEmpty(user.PasswordChangeUndoPreviousHash)) return false;

        user.PasswordHash = user.PasswordChangeUndoPreviousHash;
        user.PasswordChangeUndoTokenHash = null;
        user.PasswordChangeUndoExpiresAt = null;
        user.PasswordChangeUndoPreviousHash = null;
        await _users.ReplaceAsync(user, ct);

        // Revoke every session — the attacker may still hold one — so they're forced out.
        await _refreshTokens.RevokeAllForUserAsync(user.Id, "password_change_undone", ct);
        return true;
    }

    private async Task<User?> FindUserByUndoHashAsync(string hash, CancellationToken ct)
    {
        // Linear scan via repository helper. The user collection is small here —
        // when it grows, swap for an indexed lookup or a dedicated repo method.
        var all = await _users.ListAsync(includeInactive: false, ct);
        return all.FirstOrDefault(u => u.PasswordChangeUndoTokenHash == hash);
    }

    private Task<bool> VerifyStepUpAsync(User user, string? method, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(code))
            return Task.FromResult(false);

        switch (method)
        {
            case "totp":
                return Task.FromResult(user.TotpEnabled && _totp.Verify(user.TotpSecret ?? "", code));
            case "email":
                if (!user.EmailOtpEnabled) return Task.FromResult(false);
                var ok = _emailOtp.Verify(user.EmailOtpCodeHash, user.EmailOtpExpiresAt, code);
                if (ok) { user.EmailOtpCodeHash = null; user.EmailOtpExpiresAt = null; }
                return Task.FromResult(ok);
            case "recovery":
                if (_recovery.TryConsume(user.RecoveryCodeHashes, code, out var remaining))
                {
                    user.RecoveryCodeHashes = remaining;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            default:
                return Task.FromResult(false);
        }
    }

    public async Task<long> RevokeOtherSessionsAsync(string userId, string? currentRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentRefreshToken))
        {
            // No current cookie present — refuse rather than nuke the user out of every device.
            return 0;
        }
        var keepHash = _jwt.HashRefreshToken(currentRefreshToken);
        return await _refreshTokens.RevokeAllForUserExceptAsync(userId, keepHash, "user_revoked_others", ct);
    }

    public async Task<IReadOnlyList<ActiveSessionDto>> ListActiveSessionsAsync(
        string userId, string? currentRefreshToken, CancellationToken ct = default)
    {
        var tokens = await _refreshTokens.ListActiveForUserAsync(userId, ct);
        var currentHash = string.IsNullOrWhiteSpace(currentRefreshToken)
            ? null
            : _jwt.HashRefreshToken(currentRefreshToken);

        // Collapse rows by DeviceId so a single browser shows as one entry even
        // if multiple active tokens exist (race conditions, legacy rows). For
        // rows that predate DeviceId (null), fall back to the row's own Id so
        // they remain distinct entries.
        var groups = tokens.GroupBy(t => t.DeviceId ?? $"__row:{t.Id}");
        var rows = new List<ActiveSessionDto>();
        foreach (var g in groups)
        {
            // Pick the row representing this device: prefer the current session
            // when present, otherwise the most recently created one.
            var representative = g.OrderByDescending(t => currentHash != null && t.TokenHash == currentHash)
                                  .ThenByDescending(t => t.CreatedAt)
                                  .First();
            var (browser, os) = UserAgentParser.Parse(representative.UserAgent);
            var isCurrent = currentHash != null && g.Any(t => t.TokenHash == currentHash);
            rows.Add(new ActiveSessionDto
            {
                Id        = representative.Id,
                Browser   = browser,
                Os        = os,
                Ip        = representative.CreatedByIp,
                CreatedAt = g.Min(t => t.CreatedAt),
                ExpiresAt = g.Max(t => t.ExpiresAt),
                IsCurrent = isCurrent,
            });
        }
        return rows.OrderByDescending(r => r.IsCurrent).ThenByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<bool> RevokeSessionAsync(
        string userId, string sessionId, string? currentRefreshToken, CancellationToken ct = default)
    {
        // Don't let the user accidentally kill their own current session via the
        // "individual revoke" flow — that's what the explicit logout button is for.
        string? currentDeviceId = null;
        if (!string.IsNullOrWhiteSpace(currentRefreshToken))
        {
            var currentHash = _jwt.HashRefreshToken(currentRefreshToken);
            var current = await _refreshTokens.FindByHashAsync(currentHash, ct);
            if (current is not null)
            {
                if (current.Id == sessionId) return false;
                currentDeviceId = current.DeviceId;
            }
        }

        // The UI shows one row per device, but in the DB a device may briefly
        // own more than one active row (legacy data or rotation races). Look up
        // the target row's DeviceId and revoke every active row for that device
        // so "revoke session" empties the whole device, not just one slice.
        var active = await _refreshTokens.ListActiveForUserAsync(userId, ct);
        var target = active.FirstOrDefault(t => t.Id == sessionId);
        if (target is null) return false;
        if (!string.IsNullOrWhiteSpace(target.DeviceId))
        {
            // Belt-and-braces: refuse if the target device is somehow the current device.
            if (currentDeviceId != null && target.DeviceId == currentDeviceId) return false;
            var n = await _refreshTokens.RevokeActiveByDeviceAsync(userId, target.DeviceId, "user_revoked_one", ct);
            return n > 0;
        }
        return await _refreshTokens.RevokeOneByIdAsync(sessionId, userId, "user_revoked_one", ct);
    }

    private async Task<AuthResult> IssueTokensAsync(
        User user,
        bool rememberMe,
        string? ip,
        string? userAgent,
        string? deviceId,
        CancellationToken ct)
    {
        // Resolve the effective device id: cookie wins, else try to recover it
        // from an active token with a matching User-Agent (iOS Safari ITP and
        // similar privacy features drop our cross-site slk_did cookie, so a
        // logout+login from the same browser otherwise stacks a new row).
        // Last resort: mint a fresh id.
        var effectiveDeviceId = deviceId;
        if (string.IsNullOrWhiteSpace(effectiveDeviceId))
        {
            var actives = await _refreshTokens.ListActiveForUserAsync(user.Id, ct);
            var match = actives.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.DeviceId)
                && !string.IsNullOrWhiteSpace(t.UserAgent)
                && string.Equals(t.UserAgent, userAgent, StringComparison.Ordinal));
            effectiveDeviceId = match?.DeviceId ?? Guid.NewGuid().ToString("N");
        }

        // Per-device single-session invariant: revoke any lingering active
        // tokens for this device so the new one is the only active row.
        await _refreshTokens.RevokeActiveByDeviceAsync(user.Id, effectiveDeviceId, "superseded_same_device", ct);

        var (access, _) = _jwt.GenerateAccessToken(user);
        var (refreshPlain, refreshHash, refreshExp) = _jwt.GenerateRefreshToken(rememberMe);

        await _refreshTokens.InsertAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExp,
            CreatedByIp = ip,
            UserAgent = userAgent,
            DeviceId = effectiveDeviceId,
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
            RememberMe: rememberMe,
            DeviceId: effectiveDeviceId);
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
    };

    private static string GenerateOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
