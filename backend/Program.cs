using System.Reflection;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddHostedService<CellLockSweeperService>();
builder.Services.AddHostedService<AuditWriterService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jwtSettings.Secret)
                    ? new string('x', 32)
                    : jwtSettings.Secret)),
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
        };
    });

builder.Services.AddAuthorization();

// ─── CORS ────────────────────────────────────────────────────────────────────
var corsSettings = builder.Configuration
    .GetSection(CorsSettings.SectionName)
    .Get<CorsSettings>() ?? new CorsSettings();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsSettings.PolicyName, policy =>
    {
        if (corsSettings.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsSettings.AllowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
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
