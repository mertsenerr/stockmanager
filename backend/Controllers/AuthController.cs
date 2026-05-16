using Fido2NetLib;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SayimLink.Api.Common;
using SayimLink.Api.Configuration;
using SayimLink.Api.Dtos.Auth;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;
using SayimLink.Api.Services.TwoFactor;

namespace SayimLink.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookieName = "slk_rt";
    private const string RefreshCookiePath = "/api/auth";
    private const string DeviceCookieName = "slk_did";
    private static readonly TimeSpan DeviceCookieLifetime = TimeSpan.FromDays(730);

    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly IValidator<RegisterSayimBaskaniRequest> _registerBaskaniValidator;
    private readonly IValidator<RegisterKullaniciRequest> _registerKullaniciValidator;
    private readonly IValidator<VerifyEmailRequest> _verifyEmailValidator;
    private readonly IValidator<ResendVerificationRequest> _resendVerificationValidator;
    private readonly IValidator<UpdateProfileRequest> _updateProfileValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
    private readonly IJwtService _jwt;
    private readonly IEmailService _email;
    private readonly ITotpService _totp;
    private readonly IEmailOtpService _emailOtp;
    private readonly IWebAuthnService _webauthn;
    private readonly IRecoveryCodeService _recovery;
    private readonly ITurnstileService _turnstile;
    private readonly ITotpSecretProtector _totpProtector;
    private readonly HashSet<string> _allowedOrigins;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IAuditService audit,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        IValidator<RegisterSayimBaskaniRequest> registerBaskaniValidator,
        IValidator<RegisterKullaniciRequest> registerKullaniciValidator,
        IValidator<VerifyEmailRequest> verifyEmailValidator,
        IValidator<ResendVerificationRequest> resendVerificationValidator,
        IValidator<UpdateProfileRequest> updateProfileValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator,
        IJwtService jwt,
        IEmailService email,
        ITotpService totp,
        IEmailOtpService emailOtp,
        IWebAuthnService webauthn,
        IRecoveryCodeService recovery,
        ITurnstileService turnstile,
        ITotpSecretProtector totpProtector,
        IOptions<CorsSettings> corsOptions)
    {
        _auth = auth;
        _users = users;
        _audit = audit;
        _loginValidator = loginValidator;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _registerBaskaniValidator = registerBaskaniValidator;
        _registerKullaniciValidator = registerKullaniciValidator;
        _verifyEmailValidator = verifyEmailValidator;
        _resendVerificationValidator = resendVerificationValidator;
        _updateProfileValidator = updateProfileValidator;
        _changePasswordValidator = changePasswordValidator;
        _jwt = jwt;
        _email = email;
        _totp = totp;
        _emailOtp = emailOtp;
        _webauthn = webauthn;
        _recovery = recovery;
        _turnstile = turnstile;
        _totpProtector = totpProtector;
        _allowedOrigins = new HashSet<string>(
            corsOptions.Value.AllowedOrigins.Select(o => o.TrimEnd('/')),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Same-origin guard for cookie-only endpoints (refresh, logout). The
    /// HttpOnly slk_rt cookie is sent automatically on cross-site POSTs because
    /// SameSite=None is required for the Cloudflare Pages→Render setup; without this check
    /// an attacker page could trigger token rotation or sign-out as a blind side
    /// effect. Browsers won't let scripts forge the Origin header, so allow-list
    /// matching is equivalent to a CSRF token here.</summary>
    private bool IsAllowedOrigin()
    {
        // Empty allow-list means start-up guard already rejected boot — defensive.
        if (_allowedOrigins.Count == 0) return false;
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            // Some Same-origin browsers omit Origin on POST; fall back to Referer.
            var referer = Request.Headers.Referer.ToString();
            if (string.IsNullOrEmpty(referer)) return false;
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refUri)) return false;
            origin = $"{refUri.Scheme}://{refUri.Authority}";
        }
        return _allowedOrigins.Contains(origin.TrimEnd('/'));
    }

    private async Task<bool> CaptchaOkAsync(string? token, CancellationToken ct) =>
        await _turnstile.VerifyAsync(token, GetIp(), ct);

    /// <summary>Frontend reads this once on bootstrap to know whether to render
    /// the Turnstile widget on auth pages, and which site key to use.</summary>
    [HttpGet("captcha/config")]
    public IActionResult CaptchaConfig() =>
        Ok(new { enabled = _turnstile.Enabled, siteKey = _turnstile.SiteKey });

    private string? AuthedUserId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpPost("register/sayim-baskani")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> RegisterSayimBaskani(
        [FromBody] RegisterSayimBaskaniRequest request, CancellationToken ct)
    {
        if (!await CaptchaOkAsync(request.TurnstileToken, ct))
            return BadRequest(new { message = "Robot doğrulaması başarısız. Lütfen tekrar deneyin." });
        var validation = await _registerBaskaniValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation);

        var result = await _auth.RegisterSayimBaskaniAsync(request, ct);
        if (!result.Success)
            return Conflict(new { message = result.FailureReason ?? "Kayıt başarısız." });

        if (result.User is not null)
        {
            _audit.Enqueue(_audit.Build(
                AuditAksiyonlari.KullaniciCreate, kullaniciId: result.User.Id,
                kullaniciAdi: result.User.AdSoyad, rol: result.User.Rol,
                hedef: "user", hedefId: result.User.Id, ip: GetIp(), userAgent: GetUserAgent(),
                yeniDeger: $"sayim-baskani register · {result.User.Email}"));
        }
        // Generic response — never tell the caller whether the address was new.
        return Ok(new { message = "Kayıt isteğiniz alındı. Devam etmek için e-postanı kontrol et." });
    }

    [HttpPost("register/kullanici")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> RegisterKullanici(
        [FromBody] RegisterKullaniciRequest request, CancellationToken ct)
    {
        if (!await CaptchaOkAsync(request.TurnstileToken, ct))
            return BadRequest(new { message = "Robot doğrulaması başarısız. Lütfen tekrar deneyin." });
        var validation = await _registerKullaniciValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation);

        var result = await _auth.RegisterKullaniciAsync(request, ct);
        if (!result.Success)
            return Conflict(new { message = result.FailureReason ?? "Kayıt başarısız." });

        if (result.User is not null)
        {
            _audit.Enqueue(_audit.Build(
                AuditAksiyonlari.KullaniciCreate, kullaniciId: result.User.Id,
                kullaniciAdi: result.User.AdSoyad, rol: result.User.Rol,
                hedef: "user", hedefId: result.User.Id, ip: GetIp(), userAgent: GetUserAgent(),
                yeniDeger: $"kullanici register · {result.User.Email}"));
        }
        return Ok(new
        {
            message = "Kayıt isteğiniz alındı. E-posta adresinizi doğruladıktan sonra giriş yapabilirsiniz.",
        });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!await CaptchaOkAsync(request.TurnstileToken, ct))
            return BadRequest(new { message = "Robot doğrulaması başarısız. Lütfen tekrar deneyin." });
        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var inboundDeviceId = ReadDeviceCookie();
        var result = await _auth.LoginAsync(request, GetIp(), GetUserAgent(), inboundDeviceId, ct);

        // Password verified but 2FA required → return pending token, no cookies issued.
        if (result.Success && result.TwoFactorPendingToken is not null)
        {
            return Ok(new TwoFactorRequiredResponse
            {
                RequiresTwoFactor = true,
                PendingToken      = result.TwoFactorPendingToken,
                AvailableMethods  = result.TwoFactorAvailableMethods?.ToList() ?? [],
            });
        }

        if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
        {
            _audit.Enqueue(_audit.Build(
                AuditAksiyonlari.LoginFail,
                kullaniciId: null, kullaniciAdi: request.Email, rol: null,
                hedef: "auth", ip: GetIp(), userAgent: GetUserAgent(), basarili: false));

            if (result.FailureCode is AuthFailureCodes.AccountLocked)
            {
                var retryAfter = result.LockedRetryAfterSeconds ?? 60;
                Response.Headers["Retry-After"] = retryAfter.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = result.FailureReason ?? "Hesap geçici olarak kilitli.",
                    code = AuthFailureCodes.AccountLocked,
                    retryAfterSeconds = retryAfter,
                });
            }

            var body = new
            {
                message = result.FailureReason ?? "Giriş başarısız.",
                code = result.FailureCode ?? AuthFailureCodes.InvalidCredentials,
            };
            return result.FailureCode is AuthFailureCodes.EmailNotVerified
                ? StatusCode(StatusCodes.Status403Forbidden, body)
                : Unauthorized(body);
        }

        SetRefreshCookie(result.RefreshTokenPlaintext, result.RefreshTokenExpiresAt!.Value);
        if (!string.IsNullOrWhiteSpace(result.DeviceId)) WriteDeviceCookie(result.DeviceId);
        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.LoginSuccess,
            result.Response.User.Id, result.Response.User.AdSoyad, result.Response.User.Rol,
            hedef: "auth", ip: GetIp(), userAgent: GetUserAgent()));
        return Ok(result.Response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!IsAllowedOrigin())
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Geçersiz origin." });

        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = "Refresh token bulunamadı." });

        try
        {
            var inboundDeviceId = ReadDeviceCookie();
            var result = await _auth.RefreshAsync(refreshToken, GetIp(), GetUserAgent(), inboundDeviceId, ct);
            if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
            {
                ClearRefreshCookie();
                return Unauthorized(new { message = result.FailureReason ?? "Oturum yenilenemedi." });
            }

            SetRefreshCookie(result.RefreshTokenPlaintext, result.RefreshTokenExpiresAt!.Value);
            if (!string.IsNullOrWhiteSpace(result.DeviceId)) WriteDeviceCookie(result.DeviceId);
            return Ok(result.Response);
        }
        catch (Exception)
        {
            // DB / cold-start kaynaklı hatalarda da 500 değil — istemci anonim olarak
            // login'e yönlensin, takılıp kalmasın.
            ClearRefreshCookie();
            return Unauthorized(new { message = "Oturum yenilenemedi." });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!IsAllowedOrigin())
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Geçersiz origin." });

        if (Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            && !string.IsNullOrWhiteSpace(refreshToken))
        {
            await _auth.LogoutAsync(refreshToken, ct);
        }
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var dto = await _auth.GetCurrentUserAsync(userId, ct);
        return dto is null ? Unauthorized() : Ok(dto);
    }

    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var validation = await _updateProfileValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var dto = await _auth.UpdateProfileAsync(userId, request, ct);
        if (dto is null) return Unauthorized();

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciUpdate, kullaniciId: userId,
            kullaniciAdi: dto.AdSoyad, rol: dto.Rol,
            hedef: "user", hedefId: userId, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"profile self-update · {dto.AdSoyad}"));
        return Ok(dto);
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (!await CaptchaOkAsync(request.TurnstileToken, ct))
            return BadRequest(new { message = "Robot doğrulaması başarısız. Lütfen tekrar deneyin." });
        var validation = await _changePasswordValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        Request.Cookies.TryGetValue(RefreshCookieName, out var currentRefresh);
        var result = await _auth.ChangePasswordAsync(userId, request, currentRefresh, ct);
        if (!result.Success)
            return BadRequest(new { message = result.FailureReason ?? "Parola değiştirilemedi." });

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciUpdate, kullaniciId: userId,
            kullaniciAdi: User.Identity?.Name ?? "", rol: "",
            hedef: "user", hedefId: userId, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: "password changed (other sessions revoked)"));
        return NoContent();
    }

    [HttpPost("sessions/revoke-others")]
    [Authorize]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        Request.Cookies.TryGetValue(RefreshCookieName, out var currentRefresh);
        var revoked = await _auth.RevokeOtherSessionsAsync(userId, currentRefresh, ct);

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciUpdate, kullaniciId: userId,
            kullaniciAdi: User.Identity?.Name ?? "", rol: "",
            hedef: "session", hedefId: userId, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"revoked {revoked} other session(s)"));
        return Ok(new { revoked });
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        Request.Cookies.TryGetValue(RefreshCookieName, out var currentRefresh);
        var sessions = await _auth.ListActiveSessionsAsync(uid, currentRefresh, ct);
        return Ok(sessions);
    }

    [HttpDelete("sessions/{sessionId}")]
    [Authorize]
    public async Task<IActionResult> RevokeSession(string sessionId, CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        Request.Cookies.TryGetValue(RefreshCookieName, out var currentRefresh);
        var ok = await _auth.RevokeSessionAsync(uid, sessionId, currentRefresh, ct);
        if (!ok) return NotFound(new { message = "Oturum bulunamadı veya bu cihazın oturumu silinemez." });

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciUpdate, kullaniciId: uid,
            kullaniciAdi: User.Identity?.Name ?? "", rol: "",
            hedef: "session", hedefId: sessionId, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: "revoked one session"));
        return NoContent();
    }

    // ─── 2FA: status + management (authenticated) ─────────────────────────────
    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<IActionResult> TwoFactorStatus(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();
        return Ok(new TwoFactorStatusDto
        {
            TotpEnabled              = user.TotpEnabled,
            EmailOtpEnabled          = user.EmailOtpEnabled,
            WebAuthnEnabled          = user.WebAuthnCredentials.Count > 0,
            WebAuthnCredentialCount  = user.WebAuthnCredentials.Count,
            RecoveryCodesRemaining   = user.RecoveryCodeHashes.Count,
        });
    }

    /// <summary>Step-up guard for every 2FA enroll/disable/regen endpoint.
    /// Re-verifies password plus the existing 2FA factor (if any) so an attacker
    /// who only stole a session can't silently rotate the second-factor scheme
    /// out from under the real owner. Returns the post-verify User on success
    /// (the caller persists), or an IActionResult to short-circuit with.</summary>
    private async Task<(User? user, IActionResult? error)> RequireStepUpAsync(
        TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return (null, Unauthorized());
        var (ok, user) = await _auth.VerifyStepUpForUserAsync(
            uid, req.CurrentPassword, req.TwoFactorMethod, req.TwoFactorCode, ct);
        if (!ok)
            return (null, BadRequest(new { message = "Mevcut parola veya ikinci faktör doğrulaması hatalı." }));
        return (user, null);
    }

    // ─── 2FA: TOTP ────────────────────────────────────────────────────────────
    [HttpPost("2fa/totp/setup")]
    [Authorize]
    public async Task<IActionResult> TotpSetup([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();

        var (secret, url, qr) = _totp.GenerateSecret("SynCompare", user.Email);
        // Stage the secret on the user record but do NOT enable until the user proves they
        // can read codes from it via /enable. Stored encrypted so a DB dump alone is not
        // enough to clone the authenticator.
        user.TotpSecret = _totpProtector.Protect(secret);
        user.TotpEnabled = false;
        await _auth.ReplaceUserAsync(user, ct);
        return Ok(new TotpSetupResponse { Secret = secret, OtpAuthUrl = url, QrPngDataUri = qr });
    }

    [HttpPost("2fa/totp/enable")]
    [Authorize]
    public async Task<IActionResult> TotpEnable([FromBody] TotpEnableRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null || string.IsNullOrEmpty(user.TotpSecret))
            return BadRequest(new { message = "Önce setup adımını çalıştırın." });
        var plainSecret = _totpProtector.Unprotect(user.TotpSecret);
        if (!_totp.Verify(plainSecret, req.Code))
            return BadRequest(new { message = "Kod hatalı." });
        // Opportunistic migration: if the stored value was legacy plaintext, re-write it
        // encrypted now that we know it verifies cleanly.
        if (_totpProtector.IsLegacy(user.TotpSecret))
            user.TotpSecret = _totpProtector.Protect(plainSecret);
        user.TotpEnabled = true;
        var (codes, hashes) = EnsureRecoveryCodes(user);
        await _auth.ReplaceUserAsync(user, ct);
        return Ok(new RecoveryCodesResponse { Codes = codes });
    }

    [HttpPost("2fa/totp/disable")]
    [Authorize]
    public async Task<IActionResult> TotpDisable([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        user.TotpEnabled = false;
        user.TotpSecret = null;
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    // ─── 2FA: Email OTP ───────────────────────────────────────────────────────
    [HttpPost("2fa/email/enable")]
    [Authorize]
    public async Task<IActionResult> EmailOtpEnable([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        user.EmailOtpEnabled = true;
        EnsureRecoveryCodes(user);
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    [HttpPost("2fa/email/disable")]
    [Authorize]
    public async Task<IActionResult> EmailOtpDisable([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        user.EmailOtpEnabled = false;
        user.EmailOtpCodeHash = null;
        user.EmailOtpExpiresAt = null;
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    /// <summary>Public — caller passes the pending token, server sends an OTP to the
    /// account's email and stores its hash. Called from the 2FA verification screen.</summary>
    [HttpPost("2fa/email/send")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> EmailOtpSend([FromBody] PendingTokenRequest req, CancellationToken ct)
    {
        var pending = _jwt.ValidateTwoFactorPendingToken(req.PendingToken);
        if (pending is null) return Unauthorized(new { message = "Pending token geçersiz." });
        var user = await _auth.GetUserAsync(pending.Value.userId, ct);
        if (user is null || !user.EmailOtpEnabled)
            return BadRequest(new { message = "E-posta OTP açık değil." });

        // Per-user throttling. The IP rate limiter alone is not enough because the
        // pending-token holder can rotate IPs to keep generating OTPs — every fresh
        // code overwrites the previous one, so the legitimate user's inbox fills up
        // with stale codes and they can't tell which is the live one. Also caps
        // Resend quota burn.
        const int CooldownSeconds = 60;
        const int DailyCap = 10;
        var now = DateTime.UtcNow;
        if (user.EmailOtpLastSentAt is not null &&
            (now - user.EmailOtpLastSentAt.Value).TotalSeconds < CooldownSeconds)
        {
            var retry = (int)Math.Ceiling(CooldownSeconds - (now - user.EmailOtpLastSentAt.Value).TotalSeconds);
            Response.Headers["Retry-After"] = retry.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Çok sık istek. Lütfen birkaç saniye sonra tekrar deneyin.",
                retryAfterSeconds = retry,
            });
        }

        // 24-hour rolling window cap so even a slow drip (one per minute for hours)
        // hits a ceiling.
        var windowStart = user.EmailOtpDayWindowStart ?? now;
        var dayCount = user.EmailOtpDayCount;
        if ((now - windowStart).TotalHours >= 24)
        {
            windowStart = now;
            dayCount = 0;
        }
        if (dayCount >= DailyCap)
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Bugünkü 2FA e-posta kotası doldu. Lütfen başka bir 2FA yöntemi deneyin.",
            });

        var (plain, hash, expires) = _emailOtp.Generate();
        await _users.SetEmailOtpAsync(user.Id, hash, expires, ct);
        await _users.RecordEmailOtpSendAsync(user.Id, now, windowStart, dayCount + 1, ct);

        await _email.SendTwoFactorCodeAsync(user.Email, user.AdSoyad, plain, ct);
        return Ok(new { sent = true });
    }

    // ─── 2FA: WebAuthn ────────────────────────────────────────────────────────
    [HttpPost("2fa/webauthn/register/options")]
    [Authorize]
    public async Task<IActionResult> WebAuthnRegisterOptions([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        var opts = await _webauthn.StartRegistrationAsync(user, WebAuthnSessionKey(user.Id), ct);
        return Content(opts.ToJson(), "application/json");
    }

    [HttpPost("2fa/webauthn/register/complete")]
    [Authorize]
    public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterCompleteRequest req, CancellationToken ct)
    {
        // Re-check step-up here too — the options endpoint already verified it,
        // but the WebAuthn challenge cache is keyed by user id, so a leaked
        // session that races to /complete (with the original user's authenticator
        // proof) shouldn't be able to skip the password gate.
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();

        try
        {
            var cred = await _webauthn.CompleteRegistrationAsync(user, WebAuthnSessionKey(user.Id), req.Response, ct);
            cred.Nickname = string.IsNullOrWhiteSpace(req.Nickname) ? null : req.Nickname.Trim();
            user.WebAuthnCredentials.Add(cred);
            EnsureRecoveryCodes(user);
            await _auth.ReplaceUserAsync(user, ct);
            return Ok(new { id = cred.CredentialId, nickname = cred.Nickname });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("2fa/webauthn/auth/options")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> WebAuthnAuthOptions([FromBody] PendingTokenRequest req, CancellationToken ct)
    {
        var pending = _jwt.ValidateTwoFactorPendingToken(req.PendingToken);
        if (pending is null) return Unauthorized(new { message = "Pending token geçersiz." });
        var user = await _auth.GetUserAsync(pending.Value.userId, ct);
        if (user is null || user.WebAuthnCredentials.Count == 0)
            return BadRequest(new { message = "WebAuthn açık değil." });
        var opts = _webauthn.StartAssertion(user, WebAuthnSessionKey(user.Id));
        return Content(opts.ToJson(), "application/json");
    }

    [HttpPost("2fa/webauthn/{credentialId}/delete")]
    [Authorize]
    public async Task<IActionResult> WebAuthnDelete(string credentialId, [FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        // Switched DELETE → POST so the step-up body fits cleanly. Removing a
        // passkey shouldn't bypass the same enroll/disable gate.
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        var removed = user.WebAuthnCredentials.RemoveAll(c => c.CredentialId == credentialId);
        if (removed == 0) return NotFound();
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    [HttpGet("2fa/webauthn")]
    [Authorize]
    public async Task<IActionResult> WebAuthnList(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();
        return Ok(user.WebAuthnCredentials.Select(c => new WebAuthnCredentialDto
        {
            Id = c.CredentialId, Nickname = c.Nickname, CreatedAt = c.CreatedAt,
        }));
    }

    // ─── 2FA: Recovery codes ──────────────────────────────────────────────────
    [HttpPost("2fa/recovery-codes/regenerate")]
    [Authorize]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] TwoFactorStepUpRequest req, CancellationToken ct)
    {
        var (user, err) = await RequireStepUpAsync(req, ct);
        if (err is not null) return err;
        if (user is null) return Unauthorized();
        var (codes, hashes) = _recovery.Generate();
        user.RecoveryCodeHashes = hashes;
        await _auth.ReplaceUserAsync(user, ct);
        return Ok(new RecoveryCodesResponse { Codes = codes });
    }

    // ─── 2FA: verify (completes login) ────────────────────────────────────────
    [HttpPost("2fa/verify")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> TwoFactorVerify([FromBody] TwoFactorVerifyRequest req, CancellationToken ct)
    {
        var pending = _jwt.ValidateTwoFactorPendingToken(req.PendingToken);
        if (pending is null) return Unauthorized(new { message = "Pending token geçersiz veya süresi dolmuş." });
        var user = await _auth.GetUserAsync(pending.Value.userId, ct);
        if (user is null || !user.AktifMi) return Unauthorized();

        // Per-user lockout: rotating-IP brute-force on the OTP space is the realistic
        // post-password-leak attack vector; the IP rate limiter alone doesn't stop it.
        if (user.TwoFactorLockedUntil is not null && user.TwoFactorLockedUntil > DateTime.UtcNow)
        {
            var retryAfter = (int)Math.Ceiling((user.TwoFactorLockedUntil.Value - DateTime.UtcNow).TotalSeconds);
            Response.Headers["Retry-After"] = retryAfter.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Çok fazla hatalı 2FA denemesi. Lütfen biraz sonra tekrar deneyin.",
                retryAfterSeconds = retryAfter,
            });
        }

        // Track side-effects that need a full ReplaceUserAsync (TOTP secret re-encrypt
        // after legacy decrypt; WebAuthn counter bump). Everything else is now done with
        // atomic Mongo updates so concurrent flows don't clobber each other.
        var ok = false;
        var needsFullReplace = false;
        string? consumedRecoveryHash = null;

        switch (req.Method)
        {
            case TwoFactorMethods.Totp:
                if (user.TotpEnabled && !string.IsNullOrEmpty(user.TotpSecret))
                {
                    var totpPlain = _totpProtector.Unprotect(user.TotpSecret);
                    ok = _totp.Verify(totpPlain, req.Code ?? "");
                    if (ok && _totpProtector.IsLegacy(user.TotpSecret))
                    {
                        user.TotpSecret = _totpProtector.Protect(totpPlain);
                        needsFullReplace = true;
                    }
                }
                break;
            case TwoFactorMethods.Email:
                ok = user.EmailOtpEnabled && _emailOtp.Verify(user.EmailOtpCodeHash, user.EmailOtpExpiresAt, req.Code ?? "");
                break;
            case TwoFactorMethods.Recovery:
                if (_recovery.TryMatch(user.RecoveryCodeHashes, req.Code ?? "", out var matchedHash) && matchedHash is not null)
                {
                    consumedRecoveryHash = matchedHash;
                    ok = true;
                }
                break;
            case TwoFactorMethods.WebAuthn:
                if (req.AssertionResponse is null || user.WebAuthnCredentials.Count == 0) break;
                try
                {
                    var (matched, newCounter) = await _webauthn.CompleteAssertionAsync(
                        user, WebAuthnSessionKey(user.Id), req.AssertionResponse, ct);
                    matched.SignatureCounter = newCounter;
                    needsFullReplace = true;
                    ok = true;
                }
                catch { ok = false; }
                break;
        }

        if (!ok)
        {
            // Atomic $inc — two parallel guesses can't both miss the threshold check.
            var newCount = await _users.IncrementTwoFactorFailedAttemptsAsync(user.Id, ct);
            if (newCount >= 5)
                await _users.ApplyTwoFactorLockoutAsync(user.Id, DateTime.UtcNow.AddMinutes(15), ct);
            return Unauthorized(new { message = "İkinci faktör doğrulaması başarısız." });
        }

        // Success path: apply side-effects atomically where we can. WebAuthn counter
        // and TOTP re-encrypt still need the full replace because they mutate nested
        // structures the driver can't path-set without bespoke filters.
        if (req.Method == TwoFactorMethods.Email)
            await _users.ClearEmailOtpAsync(user.Id, ct);
        if (consumedRecoveryHash is not null)
            await _users.ConsumeRecoveryCodeAsync(user.Id, consumedRecoveryHash, ct);
        await _users.ClearTwoFactorFailureStateAsync(user.Id, ct);
        if (needsFullReplace)
            await _auth.ReplaceUserAsync(user, ct);
        var inboundDeviceId = ReadDeviceCookie();
        var result = await _auth.CompleteTwoFactorLoginAsync(user.Id, pending.Value.rememberMe, GetIp(), GetUserAgent(), inboundDeviceId, ct);
        if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
            return Unauthorized(new { message = "Oturum açılamadı." });

        SetRefreshCookie(result.RefreshTokenPlaintext, result.RefreshTokenExpiresAt!.Value);
        if (!string.IsNullOrWhiteSpace(result.DeviceId)) WriteDeviceCookie(result.DeviceId);
        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.LoginSuccess,
            result.Response.User.Id, result.Response.User.AdSoyad, result.Response.User.Rol,
            hedef: "auth", ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"2fa method={req.Method}"));
        return Ok(result.Response);
    }

    /// <summary>Auto-generates account-wide recovery codes when a user enables their first
    /// 2FA method, so they can never lock themselves out. Returns the plaintext list when
    /// a fresh batch is created (caller can show them once); empty list otherwise.</summary>
    private (List<string> plaintext, List<string> hashes) EnsureRecoveryCodes(User user)
    {
        if (user.RecoveryCodeHashes.Count > 0) return ([], user.RecoveryCodeHashes);
        var (plain, hashes) = _recovery.Generate();
        user.RecoveryCodeHashes = hashes;
        return (plain, hashes);
    }

    [HttpPost("password-change/undo")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> UndoPasswordChange([FromBody] PasswordChangeUndoRequest request, CancellationToken ct)
    {
        var ok = await _auth.UndoPasswordChangeAsync(request.Token, ct);
        if (!ok) return BadRequest(new { message = "Geri alma bağlantısı geçersiz veya süresi dolmuş." });
        return Ok(new { message = "Parola değişikliği geri alındı. Tüm cihazlardan çıkış yapıldı, eski parolanla tekrar giriş yapabilirsin." });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        if (!await CaptchaOkAsync(request.TurnstileToken, ct))
            return BadRequest(new { message = "Robot doğrulaması başarısız. Lütfen tekrar deneyin." });
        var validation = await _forgotValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        await _auth.RequestPasswordResetAsync(request.Email, ct);
        return Ok(new { message = "Eğer adres kayıtlıysa sıfırlama linki gönderildi." });
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var validation = await _verifyEmailValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var ok = await _auth.VerifyEmailAsync(request.Token, ct);
        return ok
            ? Ok(new { message = "E-posta doğrulandı. Giriş yapabilirsiniz." })
            : BadRequest(new { message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş." });
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request, CancellationToken ct)
    {
        var validation = await _resendVerificationValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        await _auth.RequestEmailVerificationAsync(request.Email, ct);
        // Generic response — never reveal whether the email exists or has already been verified.
        return Ok(new { message = "Eğer adres kayıtlıysa ve doğrulanmamışsa yeni bir bağlantı gönderildi." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var validation = await _resetValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var ok = await _auth.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return ok
            ? Ok(new { message = "Parola güncellendi." })
            : BadRequest(new { message = "Token geçersiz veya süresi dolmuş." });
    }

    private void SetRefreshCookie(string token, DateTime expiresAt) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = RefreshCookiePath,
            Expires = new DateTimeOffset(expiresAt, TimeSpan.Zero),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = RefreshCookiePath,
        });

    /// <summary>Returns the inbound device-id cookie if present, else null.
    /// Service-side resolution (UA-match fallback, mint fresh) takes over from
    /// here so the controller doesn't decide identity by itself.</summary>
    private string? ReadDeviceCookie()
    {
        Request.Cookies.TryGetValue(DeviceCookieName, out var existing);
        return string.IsNullOrWhiteSpace(existing) ? null : existing;
    }

    /// <summary>Isolation key for cached WebAuthn challenges so two browsers (or
    /// two tabs without the device cookie set yet) under the same user account
    /// can enroll passkeys in parallel without their challenges colliding.</summary>
    private string WebAuthnSessionKey(string userId) =>
        ReadDeviceCookie() ?? $"u:{userId}";

    /// <summary>Writes the resolved device id back. Called after an auth flow
    /// returns its effective DeviceId so the browser stays in sync with the
    /// server (matters when UA-match fallback kicked in because the cookie
    /// was dropped by ITP / private mode). Slides expiry forward each time.</summary>
    private void WriteDeviceCookie(string id) =>
        Response.Cookies.Append(DeviceCookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(DeviceCookieLifetime),
        });

    private string? GetIp() =>
        HttpContext.ClientIpForAudit();

    private string? GetUserAgent() =>
        Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    private IActionResult ValidationProblem(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors));
    }
}
