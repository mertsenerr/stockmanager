using MongoDB.Bson;
using MongoDB.Driver;
using SayimLink.Api.Models;

namespace SayimLink.Api.Services;

/// <summary>
/// Phase 2.5 — Personal tenancy refactor. Idempotent startup migration:
///
///   1. Drop the unique partial indexes on firmalar (Ad / Kisaltma) — duplicates
///      are now allowed under personal tenancy. Recreate as non-unique partial
///      indexes so query performance is preserved.
///   2. Split any organizasyon Firma that has more than one active SayimBaskani
///      attached: the oldest SayimBaskani keeps the original Firma; each other
///      SayimBaskani is given a freshly cloned Firma (same Ad + Kisaltma) marked
///      with SplitFromOriginalId = original.Id. Workers (Kullanici) stay attached
///      to the original Firma.
///   3. Revoke every still-active refresh token so split users (and anyone with
///      a stale firmaId in their JWT) re-login and get a fresh access token.
///
/// Idempotence:
///   • Index drops swallow IndexNotFound; non-unique creates are no-ops if present.
///   • Firmas with SplitFromOriginalId set are skipped, and after a single split
///     the kept original has only one SayimBaskani attached so it won't requalify.
///   • Refresh-token revocation only matches RevokedAt: null.
/// </summary>
public sealed class Phase2_5MigrationHostedService : IHostedService
{
    private const string MigrationId = "phase2_5_tenant_split";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Phase2_5MigrationHostedService> _logger;

    public Phase2_5MigrationHostedService(
        IServiceProvider serviceProvider,
        ILogger<Phase2_5MigrationHostedService> logger)
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
                _logger.LogInformation("Phase2.5 migration already applied — skipping.");
                return;
            }

            var mongo = scope.ServiceProvider.GetRequiredService<IMongoDbService>();
            await ResetFirmaIndexesAsync(mongo, cancellationToken);
            await SplitMultiUserOrgFirmasAsync(mongo, cancellationToken);
            // Token revoke runs ONCE now — previously it fired on every cold start
            // and silently logged out the entire user base after every deploy / 15
            // minute idle on Render free tier.
            await RevokeAllActiveRefreshTokensAsync(mongo, cancellationToken);
            await guard.MarkAppliedAsync(MigrationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase2.5 migration failed — service still starting");
        }
    }

    private async Task ResetFirmaIndexesAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var firmalar = mongo.Database.GetCollection<Firma>("firmalar");

        // Drop the prior unique partial indexes (idempotent).
        foreach (var name in new[] { "ix_firmalar_ad_unique_active", "ix_firmalar_kisaltma_unique_active" })
        {
            try
            {
                await firmalar.Indexes.DropOneAsync(name, ct);
                _logger.LogWarning("Phase2.5 migration: dropped unique index {Name}", name);
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexNotFound")
            {
                // Already dropped on a previous run.
            }
        }

        // Create the non-unique replacements (idempotent).
        var activeFilter = Builders<Firma>.Filter.Eq(f => f.AktifMi, true);

        await TryCreateIndexAsync(
            firmalar,
            "ix_firmalar_ad_active",
            Builders<Firma>.IndexKeys.Ascending(f => f.Ad),
            activeFilter,
            ct);

        await TryCreateIndexAsync(
            firmalar,
            "ix_firmalar_kisaltma_active",
            Builders<Firma>.IndexKeys.Ascending(f => f.Kisaltma),
            Builders<Firma>.Filter.And(
                activeFilter,
                Builders<Firma>.Filter.Ne(f => f.Kisaltma, string.Empty)),
            ct);
    }

    private async Task TryCreateIndexAsync(
        IMongoCollection<Firma> firmalar,
        string name,
        IndexKeysDefinition<Firma> keys,
        FilterDefinition<Firma> partial,
        CancellationToken ct)
    {
        try
        {
            await firmalar.Indexes.CreateOneAsync(
                new CreateIndexModel<Firma>(
                    keys,
                    new CreateIndexOptions<Firma>
                    {
                        Name = name,
                        PartialFilterExpression = partial,
                        // Unique omitted — Phase 2.5 personal tenancy allows duplicates.
                    }),
                cancellationToken: ct);
            _logger.LogInformation("Phase2.5 migration: ensured non-unique index {Name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Phase2.5 migration: failed to create index {Name} — continuing", name);
        }
    }

    private async Task SplitMultiUserOrgFirmasAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var firmalar = mongo.Database.GetCollection<Firma>("firmalar");
        var users = mongo.Database.GetCollection<User>("users");

        // Idempotence: skip Firmas already produced by a previous split.
        var candidates = await firmalar.Find(
            Builders<Firma>.Filter.And(
                Builders<Firma>.Filter.Eq(f => f.OrganizasyonMu, true),
                Builders<Firma>.Filter.Eq(f => f.AktifMi, true),
                Builders<Firma>.Filter.Or(
                    Builders<Firma>.Filter.Exists(f => f.SplitFromOriginalId, false),
                    Builders<Firma>.Filter.Eq(f => f.SplitFromOriginalId, null)))
        ).ToListAsync(ct);

        var splitsPerformed = 0;
        var newFirmasCreated = 0;

        foreach (var firma in candidates)
        {
            // Only SayimBaskani users define tenancy. Worker (Kullanici) accounts stay
            // attached to whichever SayimBaskani's Firma they already point to.
            var attachedSayimBaskanis = await users.Find(
                Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(u => u.FirmaId, firma.Id),
                    Builders<User>.Filter.Eq(u => u.Rol, Roles.SayimBaskani),
                    Builders<User>.Filter.Eq(u => u.AktifMi, true))
            ).SortBy(u => u.OlusturmaTarihi).ToListAsync(ct);

            if (attachedSayimBaskanis.Count <= 1) continue;

            // Oldest SayimBaskani keeps the original Firma.
            for (int i = 1; i < attachedSayimBaskanis.Count; i++)
            {
                var splitee = attachedSayimBaskanis[i];

                var newFirma = new Firma
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Ad = firma.Ad,
                    Kisaltma = firma.Kisaltma,
                    Tip = firma.Tip,
                    LogoUrl = firma.LogoUrl,
                    OrganizasyonMu = true,
                    AktifMi = true,
                    OlusturmaTarihi = DateTime.UtcNow,
                    OlusturanKullaniciId = splitee.Id,
                    SplitFromOriginalId = firma.Id,
                };
                await firmalar.InsertOneAsync(newFirma, cancellationToken: ct);

                await users.UpdateOneAsync(
                    Builders<User>.Filter.Eq(u => u.Id, splitee.Id),
                    Builders<User>.Update
                        .Set(u => u.FirmaId, newFirma.Id)
                        .Set(u => u.FirmaIds, new List<string> { newFirma.Id }),
                    cancellationToken: ct);

                _logger.LogInformation(
                    "Phase2.5 split: user {Email} ({UserId}) moved from firma {OldFirmaId} ('{Ad}') to new firma {NewFirmaId}",
                    splitee.Email, splitee.Id, firma.Id, firma.Ad, newFirma.Id);
                newFirmasCreated++;
            }

            splitsPerformed++;
        }

        if (splitsPerformed > 0)
        {
            _logger.LogWarning(
                "Phase2.5 migration: split {SplitCount} multi-user org firmas; created {NewCount} personal firmas",
                splitsPerformed, newFirmasCreated);
        }
        else
        {
            _logger.LogInformation("Phase2.5 migration: no multi-user org firmas to split");
        }
    }

    private async Task RevokeAllActiveRefreshTokensAsync(IMongoDbService mongo, CancellationToken ct)
    {
        var tokens = mongo.Database.GetCollection<RefreshToken>("refresh_tokens");
        var filter = Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null);
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, "phase2_5_tenant_split");
        var result = await tokens.UpdateManyAsync(filter, update, cancellationToken: ct);

        if (result.ModifiedCount > 0)
        {
            _logger.LogWarning(
                "Phase2.5 migration: revoked {Count} active refresh tokens (forces re-login)",
                result.ModifiedCount);
        }
        else
        {
            _logger.LogInformation("Phase2.5 migration: no active refresh tokens to revoke");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
