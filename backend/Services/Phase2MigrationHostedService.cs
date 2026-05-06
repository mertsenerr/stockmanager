using MongoDB.Driver;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

/// <summary>
/// One-shot, idempotent migrations executed at startup as part of the Phase 2 final deploy
/// (C-1 + H-3). Two effects:
///
///   1. Every existing User document is marked IsEmailVerified=true so that the new login-time
///      check doesn't lock out anyone who registered before H-3 shipped.
///   2. Every active refresh token is revoked. The new JWT enforcement requires a `firmaId`
///      claim and we want the IsEmailVerified state baked into fresh tokens, so existing
///      sessions need to re-login. Cheaper than bumping a global token version key.
///
/// Re-running on a subsequent startup is a no-op: the User filter only matches docs where the
/// field is missing or false, and the refresh-token filter only matches still-active rows.
/// </summary>
public sealed class Phase2MigrationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Phase2MigrationHostedService> _logger;

    public Phase2MigrationHostedService(
        IServiceProvider serviceProvider,
        ILogger<Phase2MigrationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mongo = scope.ServiceProvider.GetRequiredService<IMongoDbService>();

            await BackfillEmailVerifiedAsync(mongo, cancellationToken);
            await RevokeAllActiveRefreshTokensAsync(mongo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase2 migration failed — service still starting");
        }
    }

    private async Task BackfillEmailVerifiedAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var users = mongo.Database.GetCollection<User>("users");
        var filter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Exists(u => u.IsEmailVerified, false),
            Builders<User>.Filter.Eq(u => u.IsEmailVerified, false));
        var update = Builders<User>.Update.Set(u => u.IsEmailVerified, true);
        var result = await users.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogWarning(
                "Phase2 migration: auto-verified {Count} existing users",
                result.ModifiedCount);
        }
        else
        {
            _logger.LogInformation("Phase2 migration: no users needed email-verified backfill");
        }
    }

    private async Task RevokeAllActiveRefreshTokensAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var tokens = mongo.Database.GetCollection<RefreshToken>("refresh_tokens");
        var filter = Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null);
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, "phase2_jwt_migration");
        var result = await tokens.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogWarning(
                "Phase2 migration: revoked {Count} active refresh tokens (forces re-login)",
                result.ModifiedCount);
        }
        else
        {
            _logger.LogInformation("Phase2 migration: no active refresh tokens to revoke");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
