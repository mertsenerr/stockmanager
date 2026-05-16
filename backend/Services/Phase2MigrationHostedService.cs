using MongoDB.Driver;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

/// <summary>
/// One-shot, idempotent migrations executed at startup as part of the Phase 2 final deploy
/// (C-1 + H-3). Two effects:
///
///   1. Every pre-H-3 User document is marked IsEmailVerified=true so the new login-time
///      check doesn't lock out users who registered before email verification shipped.
///   2. Every active refresh token is revoked. The new JWT enforcement requires a `firmaId`
///      claim and we want the IsEmailVerified state baked into fresh tokens, so existing
///      sessions need to re-login. Cheaper than bumping a global token version key.
///
/// Idempotence guard: a row in <c>_migrations</c> with id <c>phase2_initial</c> short-
/// circuits subsequent runs. The previous implementation relied on the "field is false"
/// filter as its idempotence signal, but that also matched every freshly-registered,
/// not-yet-verified user on the next cold start — turning email verification into
/// "wait for the next deploy" instead of a real gate.
/// </summary>
public sealed class Phase2MigrationHostedService : IHostedService
{
    private const string MigrationId = "phase2_initial";

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
            var guard = scope.ServiceProvider.GetRequiredService<IMigrationGuard>();
            if (await guard.HasRunAsync(MigrationId, cancellationToken))
            {
                _logger.LogInformation("Phase2 migration already applied — skipping.");
                return;
            }

            var mongo = scope.ServiceProvider.GetRequiredService<IMongoDbService>();
            await BackfillEmailVerifiedAsync(mongo, cancellationToken);
            await RevokeAllActiveRefreshTokensAsync(mongo, cancellationToken);
            await guard.MarkAppliedAsync(MigrationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase2 migration failed — service still starting");
        }
    }

    private async Task BackfillEmailVerifiedAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var users = mongo.Database.GetCollection<User>("users");
        // Belt-and-braces: the _migrations guard is the primary stop, but we ALSO
        // restrict to rows with no EmailVerificationTokenHash so that even if the
        // guard row gets nuked, freshly-registered users (who always carry a
        // verification token hash) are not auto-verified into the system.
        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Exists(u => u.IsEmailVerified, false),
                Builders<User>.Filter.Eq(u => u.IsEmailVerified, false)),
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Exists(u => u.EmailVerificationTokenHash, false),
                Builders<User>.Filter.Eq(u => u.EmailVerificationTokenHash, null)));
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
