using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SayimLink.Api.Configuration;
using SayimLink.Api.Hubs;
using SayimLink.Api.Repositories;
using SayimLink.Api.Services;
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

// ─── Application services ────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoDbService, MongoDbService>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddSingleton<IFirmaRepository, FirmaRepository>();
builder.Services.AddSingleton<IMagazaRepository, MagazaRepository>();
builder.Services.AddSingleton<IAtamaRepository, AtamaRepository>();
builder.Services.AddSingleton<IOturumRepository, OturumRepository>();
builder.Services.AddSingleton<IFriendshipRepository, FriendshipRepository>();
builder.Services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IEmailService, ResendEmailService>();
builder.Services.AddSingleton<ICellLockService, CellLockService>();
builder.Services.AddSingleton<ICallRegistry, CallRegistry>();
builder.Services.AddHttpClient();

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
        // OnTokenValidated rejects tokens missing the `firmaId` claim (issued by code
        // before the Phase 2 final deploy) — the frontend's 401 path then redirects
        // to login, where the user gets a fresh token with the new shape.
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
            OnTokenValidated = ctx =>
            {
                var firmaId = ctx.Principal?.FindFirst("firmaId")?.Value;
                var role = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                // Sistem (platform super-admin, seeded with no firma) may have an empty firmaId.
                // Every other role must carry one — pre-Phase-2 tokens lacked the claim, so an
                // empty value here means a stale token and the user must re-login.
                if (string.IsNullOrEmpty(firmaId) && role != SayimLink.Api.Models.Roles.Sistem)
                {
                    ctx.Fail("Token missing firmaId claim — re-login required.");
                }
                return Task.CompletedTask;
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
app.UseSerilogRequestLogging();

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
    if (!app.Environment.IsDevelopment())
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
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
