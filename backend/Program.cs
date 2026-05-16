using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Fido2NetLib;
using SayimLink.Api.Common;
using SayimLink.Api.Configuration;
using SayimLink.Api.Hubs;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;
using SayimLink.Api.Services.TwoFactor;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─── Logging ─────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ─── Configuration binding ───────────────────────────────────────────────────
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<ResendSettings>(
    builder.Configuration.GetSection(ResendSettings.SectionName));
builder.Services.Configure<SeedSettings>(
    builder.Configuration.GetSection(SeedSettings.SectionName));
builder.Services.Configure<OzelRaporSettings>(
    builder.Configuration.GetSection(OzelRaporSettings.SectionName));
builder.Services.Configure<Fido2Settings>(
    builder.Configuration.GetSection("Fido2"));
builder.Services.Configure<TurnstileSettings>(
    builder.Configuration.GetSection(TurnstileSettings.SectionName));
builder.Services.Configure<EncryptionSettings>(
    builder.Configuration.GetSection(EncryptionSettings.SectionName));

// ─── Application services ────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoDbService, MongoDbService>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddSingleton<IFirmaRepository, FirmaRepository>();
builder.Services.AddSingleton<IMagazaRepository, MagazaRepository>();
builder.Services.AddSingleton<IAtamaRepository, AtamaRepository>();
builder.Services.AddSingleton<IOturumRepository, OturumRepository>();
builder.Services.AddSingleton<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddSingleton<IOzelRaporRepository, OzelRaporRepository>();
builder.Services.AddSingleton<IOzelRaporStorage, OzelRaporStorage>();
builder.Services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IMigrationGuard, MigrationGuard>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IEmailService, ResendEmailService>();
builder.Services.AddSingleton<ITurnstileService, TurnstileService>();
builder.Services.AddSingleton<ICellLockService, CellLockService>();
builder.Services.AddSingleton<ICallRegistry, CallRegistry>();
builder.Services.AddHttpClient();

// ─── Forwarded headers ──────────────────────────────────────────────────────
// Render (and Cloudflare in front of it) terminate TLS and proxy to the app,
// so HttpContext.Connection.RemoteIpAddress is always the LB's internal IP.
// Trust X-Forwarded-For / X-Forwarded-Proto so audit logs, rate limiting and
// active-sessions show the real client IP. Known proxy lists are cleared
// because Render's LB IPs are dynamic; the header is only used for audit/UI,
// not security decisions, so accepting unverified upstreams is acceptable.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 2; // Cloudflare → Render LB → app
});

// ─── 2FA stack ───────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRecoveryCodeService, RecoveryCodeService>();
builder.Services.AddSingleton<ITotpSecretProtector, TotpSecretProtector>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddSingleton<IEmailOtpService, EmailOtpService>();
builder.Services.AddScoped<IWebAuthnService, WebAuthnService>();
builder.Services.AddSingleton<IFido2>(_ =>
{
    var fido = builder.Configuration.GetSection("Fido2").Get<Fido2Settings>() ?? new Fido2Settings();
    return new Fido2(new Fido2Configuration
    {
        ServerDomain = fido.ServerDomain,
        ServerName   = fido.ServerName,
        Origins      = new HashSet<string>(fido.Origins),
        TimestampDriftTolerance = 300_000,
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddHostedService<AdminSeederHostedService>();
builder.Services.AddHostedService<Phase2MigrationHostedService>();
builder.Services.AddHostedService<Phase2_5MigrationHostedService>();
builder.Services.AddHostedService<CellLockSweeperService>();
builder.Services.AddHostedService<AuditWriterService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// ─── Rate limiting ───────────────────────────────────────────────────────────
// `auth-strict`   → login / register / forgot / reset (credential-stuffing surface)
// `auth-moderate` → general authenticated endpoints if we ever opt in
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-strict", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.AddPolicy("auth-moderate", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

// ─── Swagger / OpenAPI UI ────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SayımLink API",
        Version = "v1",
        Description = "Canlı, çok kullanıcılı, rol tabanlı sayım karşılaştırma platformu.",
    });

    // JWT bearer support — "Authorize" button at the top of Swagger UI.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT access token. Sadece token'ı yapıştır — \"Bearer \" öneki otomatik eklenir.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });

    // Group endpoints by controller name (default).
    options.TagActionsBy(api => new[]
    {
        api.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) && c is not null
            ? c
            : api.GroupName ?? "default",
    });
    options.DocInclusionPredicate((_, _) => true);

    // XML docs for richer descriptions on actions/DTOs.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

// ─── Authentication / Authorization ──────────────────────────────────────────
var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>() ?? new JwtSettings();

if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or shorter than 32 characters. " +
        "Set the Jwt__Secret environment variable to a high-entropy value " +
        "(e.g. `openssl rand -base64 48`). Refusing to start.");
}

// ─── Resend / outbound email — fail-fast in production ───────────────────────
// Dev path is allowed to run without Resend configured; ResendEmailService
// short-circuits and logs the reset link instead of calling the API.
var resendSettings = builder.Configuration
    .GetSection(ResendSettings.SectionName).Get<ResendSettings>() ?? new ResendSettings();

if (!builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(resendSettings.ApiKey))
        throw new InvalidOperationException(
            "Resend:ApiKey is missing. Set Resend__ApiKey env var. Refusing to start.");

    if (string.IsNullOrWhiteSpace(resendSettings.FromEmail) || !resendSettings.FromEmail.Contains('@'))
        throw new InvalidOperationException(
            "Resend:FromEmail must be a valid address on a Resend-verified domain. " +
            "Set Resend__FromEmail env var. Refusing to start.");

    if (string.IsNullOrWhiteSpace(resendSettings.PasswordResetUrlTemplate)
        || !resendSettings.PasswordResetUrlTemplate.Contains("{token}"))
        throw new InvalidOperationException(
            "Resend:PasswordResetUrlTemplate must include the literal '{token}' placeholder " +
            "(e.g. https://syncompare.com/reset-password?token={token}). " +
            "Set Resend__PasswordResetUrlTemplate env var. Refusing to start.");

    if (string.IsNullOrWhiteSpace(resendSettings.EmailVerificationUrlTemplate)
        || !resendSettings.EmailVerificationUrlTemplate.Contains("{token}"))
        throw new InvalidOperationException(
            "Resend:EmailVerificationUrlTemplate must include the literal '{token}' placeholder " +
            "(e.g. https://syncompare.com/verify-email?token={token}). " +
            "Set Resend__EmailVerificationUrlTemplate env var. Refusing to start.");

    if (string.IsNullOrWhiteSpace(resendSettings.PasswordChangeUndoUrlTemplate)
        || !resendSettings.PasswordChangeUndoUrlTemplate.Contains("{token}"))
        throw new InvalidOperationException(
            "Resend:PasswordChangeUndoUrlTemplate must include the literal '{token}' placeholder " +
            "(e.g. https://syncompare.com/password-undo?token={token}). " +
            "Without it the undo link is dropped and the change-password flow loses its " +
            "defence-in-depth notice. Set Resend__PasswordChangeUndoUrlTemplate env var. " +
            "Refusing to start.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        };
        // Allow JWT to come via the access_token query string for SignalR
        // (browser EventSource/WebSocket clients can't set Authorization headers).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
            // Stateless JWT revocation. The signing-key check has already passed
            // by the time we get here; we reject tokens whose `iat` predates the
            // owner's TokenInvalidatedAt cut-off (set when an admin pacifies the
            // user, the user changes their password, etc.). 30s in-memory cache
            // keeps the DB lookup off the per-request hot path for chatty
            // endpoints; the brief lag is acceptable because the natural access-
            // token expiry is already only 15 minutes.
            OnTokenValidated = async ctx =>
            {
                var sub = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? ctx.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(sub)) { ctx.Fail("missing-sub"); return; }

                var iatClaim = ctx.Principal!.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat)?.Value;
                if (!long.TryParse(iatClaim, out var iatUnix)) return; // tokens minted before this rollout lacked iat
                var iat = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;

                var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var cacheKey = $"tokeninv:{sub}";
                if (!cache.TryGetValue(cacheKey, out object? boxed))
                {
                    var users = ctx.HttpContext.RequestServices.GetRequiredService<SayimLink.Api.Repositories.IUserRepository>();
                    var user = await users.FindByIdAsync(sub, ctx.HttpContext.RequestAborted);
                    // Also reject any token belonging to a now-pacified user (defence-
                    // in-depth on top of the AktifMi check downstream).
                    if (user is not null && !user.AktifMi)
                    {
                        ctx.Fail("user-deactivated");
                        return;
                    }
                    // Boxed Nullable<DateTime> so the cache can tell "we looked it up
                    // and there was no cut-off" apart from "we never looked".
                    boxed = (object?)user?.TokenInvalidatedAt;
                    cache.Set(cacheKey, boxed, TimeSpan.FromSeconds(30));
                }

                if (boxed is DateTime cutoff && cutoff > iat)
                    ctx.Fail("token-invalidated");
            },
        };
    });

builder.Services.AddAuthorization();

// ─── CORS ────────────────────────────────────────────────────────────────────
var corsSettings = builder.Configuration
    .GetSection(CorsSettings.SectionName)
    .Get<CorsSettings>() ?? new CorsSettings();

if (corsSettings.AllowedOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is empty. Set Cors__AllowedOrigins__0 (and __1, __2 …) " +
        "to the explicit list of permitted frontend origins. " +
        "Refusing to start with an open CORS policy.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsSettings.PolicyName, policy =>
    {
        policy.WithOrigins(corsSettings.AllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ─── Middleware pipeline (order is locked) ───────────────────────────────────
// MUST run before anything that reads RemoteIpAddress (rate limiter, audit,
// Serilog request logging) so they see the real client IP, not Render's LB.
app.UseForwardedHeaders();

// Cloudflare puts the real client IP in CF-Connecting-IP. X-Forwarded-For can
// land with Cloudflare's own edge IP (172.70.0.0/13, 104.16.0.0/12, …) at the
// rightmost position, which is what ForwardedHeaders picks, so the audit log
// and active-sessions UI end up showing a Cloudflare IP. We cache the CF value
// on HttpContext.Items for audit / observability ONLY — Connection.RemoteIpAddress
// stays untouched so the rate limiter, SignalR transport and anything else
// making security decisions keep seeing the verified upstream IP. If an attacker
// reaches the origin URL directly (Render origin is public) and forges CF-IP,
// they can mislead the audit log but cannot evade per-IP throttling or lockouts.
app.Use(async (ctx, next) =>
{
    ctx.ResolveAuditClientIp();
    await next();
});

app.UseSerilogRequestLogging(opts =>
{
    // Default Serilog template logs RequestPath including the query string verbatim.
    // SignalR sends the JWT as ?access_token=... on the WebSocket handshake (browsers
    // can't set Authorization on WS upgrades) so the raw token would otherwise land
    // in every request log line. Mask it before it leaves the process.
    opts.MessageTemplate =
        "HTTP {RequestMethod} {SanitizedPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    opts.EnrichDiagnosticContext = (diag, http) =>
    {
        var path = http.Request.Path.ToString();
        var query = http.Request.QueryString.HasValue ? http.Request.QueryString.Value! : string.Empty;
        if (!string.IsNullOrEmpty(query))
        {
            query = System.Text.RegularExpressions.Regex.Replace(
                query, @"access_token=[^&]+", "access_token=***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        diag.Set("SanitizedPath", path + query);
    };
});

// Global exception handler — TR-localized 500, traceId, hide stack in prod.
app.UseExceptionHandler(eh =>
{
    eh.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var traceId = context.TraceIdentifier;
        logger.LogError(ex, "Unhandled exception (traceId={TraceId})", traceId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var payload = new
        {
            type = "https://sayimlink/errors/internal",
            title = "Sunucu hatası",
            status = 500,
            detail = "Beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.",
            traceId,
            error = app.Environment.IsDevelopment() ? ex?.Message : null,
        };
        await context.Response.WriteAsJsonAsync(payload);
    });
});

// Security response headers — applied to every response, including errors.
app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), microphone=(self), camera=(self), payment=()";
    if (!app.Environment.IsDevelopment())
    {
        // preload makes us eligible for the browser HSTS preload list — once added
        // there, browsers refuse plaintext requests to syncompare.com even on the
        // very first visit. Note: only add to hstspreload.org after confirming all
        // subdomains can serve HTTPS, removal can take months.
        headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";
        // API only serves JSON in production; nothing here should ever load scripts,
        // styles or framing. Frontend (Angular) ships its own CSP from Netlify.
        // default-src 'none' is the safest possible policy for a JSON API.
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
    else
    {
        // Dev: Swagger UI needs inline scripts/styles and same-origin assets to
        // render. Keep the policy loose enough not to break /swagger.
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SayımLink API v1");
        c.DocumentTitle = "SayımLink API — Swagger";
        c.DefaultModelsExpandDepth(-1); // collapse the schemas section by default
    });
}

app.UseRouting();
app.UseCors(CorsSettings.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<SayimHub>("/hubs/sayim");

try
{
    Log.Information("SayımLink API starting on {Urls}", string.Join(", ", app.Urls));
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SayımLink API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
