using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SayimLink.Api.Configuration;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Services;

public sealed class AdminSeederHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SeedSettings _settings;
    private readonly ILogger<AdminSeederHostedService> _logger;

    public AdminSeederHostedService(
        IServiceProvider serviceProvider,
        IOptions<SeedSettings> options,
        ILogger<AdminSeederHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoDbService>();
            await MigrateLegacyRolesAsync(mongo, cancellationToken);

            if (string.IsNullOrWhiteSpace(_settings.AdminEmail)
                || string.IsNullOrWhiteSpace(_settings.AdminPassword))
            {
                _logger.LogInformation("Seed admin credentials not configured — skipping seed.");
                return;
            }

            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            if (await users.AnyAdminAsync(cancellationToken))
            {
                _logger.LogInformation("Sistem admin already exists — skipping seed.");
                return;
            }

            var admin = new User
            {
                Email = _settings.AdminEmail.ToLowerInvariant(),
                AdSoyad = _settings.AdminName,
                Rol = Roles.Sistem,
                PasswordHash = hasher.Hash(_settings.AdminPassword),
                AktifMi = true,
                Onayli = true,
            };

            await users.InsertAsync(admin, cancellationToken);
            _logger.LogInformation("Seeded initial sistem admin {Email}", admin.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during admin seed/migration");
        }
    }

    /// <summary>
    /// Eski rol değerlerini (Admin/SayimYoneticisi/MagazaMuduru/Sayman) yeni
    /// 3 rollü modele (Sistem/SayimBaskani/Kullanici) taşır. Bu metod idempotent —
    /// yeni rol değerleri zaten varsa no-op olur.
    /// </summary>
    private async Task MigrateLegacyRolesAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var users = mongo.Database.GetCollection<User>("users");

        var maps = new (string Old, string New)[]
        {
            ("Admin", Roles.Sistem),
            ("SayimYoneticisi", Roles.SayimBaskani),
            ("MagazaMuduru", Roles.Kullanici),
            ("Sayman", Roles.Kullanici),
        };

        foreach (var (oldRole, newRole) in maps)
        {
            if (oldRole == newRole) continue;
            var filter = Builders<User>.Filter.Eq(u => u.Rol, oldRole);
            var update = Builders<User>.Update.Set(u => u.Rol, newRole).Set(u => u.Onayli, true);
            var result = await users.UpdateManyAsync(filter, update, cancellationToken: ct);
            if (result.ModifiedCount > 0)
                _logger.LogInformation(
                    "Migrated {Count} users from rol={Old} → rol={New}",
                    result.ModifiedCount, oldRole, newRole);
        }

        // Onayli alanı eski kayıtlarda yoksa true olarak işaretle (default).
        var fillOnayliFilter = Builders<User>.Filter.Exists(u => u.Onayli, false);
        var fillOnayliUpdate = Builders<User>.Update.Set(u => u.Onayli, true);
        await users.UpdateManyAsync(fillOnayliFilter, fillOnayliUpdate, cancellationToken: ct);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
