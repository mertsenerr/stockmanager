using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace SayimLink.Api.Services;

/// <summary>One row per applied migration. Phase2/Phase2.5 used to re-run their
/// entire bodies on every cold start because they relied on field-shape filters
/// (e.g. "find users where IsEmailVerified=false") as their idempotence signal.
/// That filter happens to also match every freshly-registered, not-yet-verified
/// user — so a cold start silently auto-verified pending registrations and let
/// callers skip the email verification flow entirely. A row in this collection
/// is the canonical "this migration already ran" signal.</summary>
public sealed class MigrationRecord
{
    [BsonId] public string Id { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
}

public interface IMigrationGuard
{
    Task<bool> HasRunAsync(string migrationId, CancellationToken ct = default);
    Task MarkAppliedAsync(string migrationId, CancellationToken ct = default);
}

public sealed class MigrationGuard : IMigrationGuard
{
    private readonly IMongoCollection<MigrationRecord> _coll;

    public MigrationGuard(IMongoDbService mongo)
    {
        _coll = mongo.Database.GetCollection<MigrationRecord>("_migrations");
    }

    public async Task<bool> HasRunAsync(string migrationId, CancellationToken ct = default)
    {
        var existing = await _coll.Find(m => m.Id == migrationId)
            .Project(m => m.Id)
            .FirstOrDefaultAsync(ct);
        return existing is not null;
    }

    public Task MarkAppliedAsync(string migrationId, CancellationToken ct = default) =>
        _coll.ReplaceOneAsync(
            m => m.Id == migrationId,
            new MigrationRecord { Id = migrationId, AppliedAt = DateTime.UtcNow },
            new ReplaceOptions { IsUpsert = true },
            ct);
}
