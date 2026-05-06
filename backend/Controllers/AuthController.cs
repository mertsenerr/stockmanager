using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SayimLink.Api.Dtos.Auth;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookieName = "slk_rt";
    private const string RefreshCookiePath = "/api/auth";

    private readonly IAuthService _auth;
    private readonly IAuditService _audit;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly IValidator<RegisterSayimBaskaniRequest> _registerBaskaniValidator;
    private readonly IValidator<RegisterKullaniciRequest> _registerKullaniciValidator;

    public AuthController(
        IAuthService auth,
        IAuditService audit,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        IValidator<RegisterSayimBaskaniRequest> registerBaskaniValidator,
        IValidator<RegisterKullaniciRequest> registerKullaniciValidator)
    {
        _auth = auth;
        _audit = audit;
        _loginValidator = loginValidator;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _registerBaskaniValidator = registerBaskaniValidator;
        _registerKullaniciValidator = registerKullaniciValidator;
    }

    [HttpPost("register/sayim-baskani")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> RegisterSayimBaskani(
        [FromBody] RegisterSayimBaskaniRequest request, CancellationToken ct)
    {
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
        var validation = await _registerKullaniciValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation);

        var result = await _auth.RegisterKullaniciAsync(request, ct);
        if (!result.Success || result.User is null)
            return Conflict(new { message = result.FailureReason ?? "Kayıt başarısız." });

        _audit.Enqueue(_audit.Build(
            AuditAksiyonlari.KullaniciCreate, kullaniciId: result.User.Id,
            kullaniciAdi: result.User.AdSoyad, rol: result.User.Rol,
            hedef: "user", hedefId: result.User.Id, ip: GetIp(), userAgent: GetUserAgent(),
            yeniDeger: $"kullanici register (pending) · {result.User.Email}"));
        return Ok(new
        {
            message = "Kayıt alındı. Sayım Başkanı onayından sonra giriş yapabilirsiniz.",
            user = result.User,
        });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        var result = await _auth.LoginAsync(request, GetIp(), GetUserAgent(), ct);
        if (!result.Success || result.Response is null || result.RefreshTokenPlaintext is null)
        {
            _audit.Enqueue(_audit.Build(
                AuditAksiyonlari.LoginFail,
                kullaniciId: null, kullaniciAdi: request.Email, rol: null,
                hedef: "auth", ip: GetIp(), userAgent: GetUserAgent(), basarili: false));
            return Unauthorized(new { message = result.FailureReason ?? "Giriş başarısız." });
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
            var result = await _auth.RefreshAsync(refreshToken, GetIp(), GetUserAgent(), ct);
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

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-strict")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var validation = await _forgotValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation);

        await _auth.RequestPasswordResetAsync(request.Email, ct);
        return Ok(new { message = "Eğer adres kayıtlıysa sıfırlama linki gönderildi." });
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
