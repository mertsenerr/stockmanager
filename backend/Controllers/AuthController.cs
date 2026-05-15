using Fido2NetLib;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SayimLink.Api.Dtos.Auth;
using SayimLink.Api.Models;
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

    public AuthController(
        IAuthService auth,
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
        ITurnstileService turnstile)
    {
        _auth = auth;
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
        if (!result.Success || result.User is null)
            return Conflict(new { message = result.FailureReason ?? "Kayıt başarısız." });

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciCreate, kullaniciId: result.User.Id,
            kullaniciAdi: result.User.AdSoyad, rol: result.User.Rol,
            hedef: "user", hedefId: result.User.Id, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"sayim-baskani register · {result.User.Email}"));
        return Ok(new { message = "Kayıt tamamlandı. Giriş yapabilirsiniz.", user = result.User });
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
        if (!result.Success || result.User is null)
            return Conflict(new { message = result.FailureReason ?? "Kayıt başarısız." });

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciCreate, kullaniciId: result.User.Id,
            kullaniciAdi: result.User.AdSoyad, rol: result.User.Rol,
            hedef: "user", hedefId: result.User.Id, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"kullanici register · {result.User.Email}"));
        return Ok(new
        {
            message = "Kayıt tamamlandı. E-posta adresinizi doğruladıktan sonra giriş yapabilirsiniz.",
            user = result.User,
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

        var deviceId = EnsureDeviceCookie();
        var result = await _auth.LoginAsync(request, GetIp(), GetUserAgent(), deviceId, ct);

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
        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.LoginSuccess,
            result.Response.User.Id, result.Response.User.AdSoyad, result.Response.User.Rol,
            hedef: "auth", ip: GetIp(), userAgent: GetUserAgent()));
        return Ok(result.Response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = "Refresh token bulunamadı." });

        try
        {
            var deviceId = EnsureDeviceCookie();
            var result = await _auth.RefreshAsync(refreshToken, GetIp(), GetUserAgent(), deviceId, ct);
            if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
            {
                ClearRefreshCookie();
                return Unauthorized(new { message = result.FailureReason ?? "Oturum yenilenemedi." });
            }

            SetRefreshCookie(result.RefreshTokenPlaintext, result.RefreshTokenExpiresAt!.Value);
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

    // ─── 2FA: TOTP ────────────────────────────────────────────────────────────
    [HttpPost("2fa/totp/setup")]
    [Authorize]
    public async Task<IActionResult> TotpSetup(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();

        var (secret, url, qr) = _totp.GenerateSecret("SynCompare", user.Email);
        // Stage the secret on the user record but do NOT enable until the user proves they
        // can read codes from it via /enable.
        user.TotpSecret = secret;
        user.TotpEnabled = false;
        await _auth.ReplaceUserAsync(user, ct);
        return Ok(new TotpSetupResponse { Secret = secret, OtpAuthUrl = url, QrPngDataUri = qr });
    }

    [HttpPost("2fa/totp/enable")]
    [Authorize]
    public async Task<IActionResult> TotpEnable([FromBody] TotpEnableRequest req, CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null || string.IsNullOrEmpty(user.TotpSecret))
            return BadRequest(new { message = "Önce setup adımını çalıştırın." });
        if (!_totp.Verify(user.TotpSecret, req.Code))
            return BadRequest(new { message = "Kod hatalı." });
        user.TotpEnabled = true;
        var (codes, hashes) = EnsureRecoveryCodes(user);
        await _auth.ReplaceUserAsync(user, ct);
        return Ok(new RecoveryCodesResponse { Codes = codes });
    }

    [HttpPost("2fa/totp/disable")]
    [Authorize]
    public async Task<IActionResult> TotpDisable(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();
        user.TotpEnabled = false;
        user.TotpSecret = null;
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    // ─── 2FA: Email OTP ───────────────────────────────────────────────────────
    [HttpPost("2fa/email/enable")]
    [Authorize]
    public async Task<IActionResult> EmailOtpEnable(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();
        user.EmailOtpEnabled = true;
        EnsureRecoveryCodes(user);
        await _auth.ReplaceUserAsync(user, ct);
        return NoContent();
    }

    [HttpPost("2fa/email/disable")]
    [Authorize]
    public async Task<IActionResult> EmailOtpDisable(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
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

        var (plain, hash, expires) = _emailOtp.Generate();
        user.EmailOtpCodeHash = hash;
        user.EmailOtpExpiresAt = expires;
        await _auth.ReplaceUserAsync(user, ct);

        await _email.SendTwoFactorCodeAsync(user.Email, user.AdSoyad, plain, ct);
        return Ok(new { sent = true });
    }

    // ─── 2FA: WebAuthn ────────────────────────────────────────────────────────
    [HttpPost("2fa/webauthn/register/options")]
    [Authorize]
    public async Task<IActionResult> WebAuthnRegisterOptions(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();
        var opts = await _webauthn.StartRegistrationAsync(user, ct);
        return Content(opts.ToJson(), "application/json");
    }

    [HttpPost("2fa/webauthn/register/complete")]
    [Authorize]
    public async Task<IActionResult> WebAuthnRegisterComplete([FromBody] WebAuthnRegisterCompleteRequest req, CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
        if (user is null) return Unauthorized();

        try
        {
            var cred = await _webauthn.CompleteRegistrationAsync(user, req.Response, ct);
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
        var opts = _webauthn.StartAssertion(user);
        return Content(opts.ToJson(), "application/json");
    }

    [HttpDelete("2fa/webauthn/{credentialId}")]
    [Authorize]
    public async Task<IActionResult> WebAuthnDelete(string credentialId, CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
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
    public async Task<IActionResult> RegenerateRecoveryCodes(CancellationToken ct)
    {
        var uid = AuthedUserId();
        if (uid is null) return Unauthorized();
        var user = await _auth.GetUserAsync(uid, ct);
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

        var ok = false;
        switch (req.Method)
        {
            case TwoFactorMethods.Totp:
                ok = user.TotpEnabled && _totp.Verify(user.TotpSecret ?? "", req.Code ?? "");
                break;
            case TwoFactorMethods.Email:
                ok = user.EmailOtpEnabled && _emailOtp.Verify(user.EmailOtpCodeHash, user.EmailOtpExpiresAt, req.Code ?? "");
                if (ok) { user.EmailOtpCodeHash = null; user.EmailOtpExpiresAt = null; }
                break;
            case TwoFactorMethods.Recovery:
                if (_recovery.TryConsume(user.RecoveryCodeHashes, req.Code ?? "", out var remaining))
                {
                    user.RecoveryCodeHashes = remaining;
                    ok = true;
                }
                break;
            case TwoFactorMethods.WebAuthn:
                if (req.AssertionResponse is null || user.WebAuthnCredentials.Count == 0) break;
                try
                {
                    var (matched, newCounter) = await _webauthn.CompleteAssertionAsync(user, req.AssertionResponse, ct);
                    matched.SignatureCounter = newCounter;
                    ok = true;
                }
                catch { ok = false; }
                break;
        }

        if (!ok)
        {
            await _auth.ReplaceUserAsync(user, ct);
            return Unauthorized(new { message = "İkinci faktör doğrulaması başarısız." });
        }

        await _auth.ReplaceUserAsync(user, ct);
        var deviceId = EnsureDeviceCookie();
        var result = await _auth.CompleteTwoFactorLoginAsync(user.Id, pending.Value.rememberMe, GetIp(), GetUserAgent(), deviceId, ct);
        if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
            return Unauthorized(new { message = "Oturum açılamadı." });

        SetRefreshCookie(result.RefreshTokenPlaintext, result.RefreshTokenExpiresAt!.Value);
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

    /// <summary>Reads the long-lived device-id cookie or mints a new one. Always
    /// (re)sets the cookie so its expiry slides forward on each auth request.
    /// Not a security control — purely a stable handle to collapse the
    /// active-sessions UI to one row per browser and let a logout+login cycle
    /// reuse the same row instead of stacking new ones.</summary>
    private string EnsureDeviceCookie()
    {
        Request.Cookies.TryGetValue(DeviceCookieName, out var existing);
        var id = string.IsNullOrWhiteSpace(existing) ? Guid.NewGuid().ToString("N") : existing;
        Response.Cookies.Append(DeviceCookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(DeviceCookieLifetime),
        });
        return id;
    }

    private string? GetIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

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
