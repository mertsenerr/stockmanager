using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IRefreshTokenRepository
{
    Task InsertAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct = default);
    Task ReplaceAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAllForUserAsync(string userId, string reason, CancellationToken ct = default);
    Task<long> RevokeAllForUserExceptAsync(string userId, string keepHash, string reason, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(string userId, CancellationToken ct = default);
    Task<bool> RevokeOneByIdAsync(string tokenId, string userId, string reason, CancellationToken ct = default);
    Task<long> RevokeActiveByDeviceAsync(string userId, string deviceId, string reason, CancellationToken ct = default);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IMongoCollection<RefreshToken> _tokens;

    public RefreshTokenRepository(IMongoDbService mongo)
    {
        _tokens = mongo.Database.GetCollection<RefreshToken>("refresh_tokens");
        // Drop the old 60d TTL index if present — Mongo refuses CreateIndex with
        // the same name but different options, so we have to clear before creating
        // the new ExpireAfter=0 version. Safe no-op if already absent.
        try { _tokens.Indexes.DropOne("ix_refresh_ttl"); }
        catch { /* IndexNotFound */ }
        _tokens.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ix_refresh_hash_unique" }),
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.UserId),
                new CreateIndexOptions { Name = "ix_refresh_user" }),
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys
                    .Ascending(t => t.UserId)
                    .Ascending(t => t.DeviceId),
                new CreateIndexOptions { Name = "ix_refresh_user_device" }),
            // TTL field is ExpiresAt itself, with ExpireAfter=0 → Mongo deletes
            // the row at the exact instant the token expires. Previously this was
            // ExpireAfter=60d which left already-expired+revoked rows hanging
            // around long after they were useful for anything except inflating
            // the collection's working set.
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions
                {
                    Name = "ix_refresh_ttl",
                    ExpireAfter = TimeSpan.Zero,
                }),
        });
    }

    public Task InsertAsync(RefreshToken token, CancellationToken ct = default) =>
        _tokens.InsertOneAsync(token, cancellationToken: ct);

    public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct = default) =>
        _tokens.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(ct)!;

    public Task ReplaceAsync(RefreshToken token, CancellationToken ct = default) =>
        _tokens.ReplaceOneAsync(t => t.Id == token.Id, token, cancellationToken: ct);

    public async Task RevokeAllForUserAsync(string userId, string reason, CancellationToken ct = default)
    {
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, reason);
        await _tokens.UpdateManyAsync(
            t => t.UserId == userId && t.RevokedAt == null,
            update,
            cancellationToken: ct);
    }

    public async Task<long> RevokeAllForUserExceptAsync(string userId, string keepHash, string reason, CancellationToken ct = default)
    {
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, reason);
        var result = await _tokens.UpdateManyAsync(
            t => t.UserId == userId && t.RevokedAt == null && t.TokenHash != keepHash,
            update,
            cancellationToken: ct);
        return result.ModifiedCount;
    }

    public async Task<IReadOnlyList<RefreshToken>> ListActiveForUserAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _tokens
            .Find(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .SortByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeOneByIdAsync(string tokenId, string userId, string reason, CancellationToken ct = default)
    {
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, reason);
        var result = await _tokens.UpdateOneAsync(
            t => t.Id == tokenId && t.UserId == userId && t.RevokedAt == null,
            update,
            cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<long> RevokeActiveByDeviceAsync(string userId, string deviceId, string reason, CancellationToken ct = default)
    {
        var update = Builders<RefreshToken>.Update
            .Set(t => t.RevokedAt, DateTime.UtcNow)
            .Set(t => t.RevokedReason, reason);
        var result = await _tokens.UpdateManyAsync(
            t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null,
            update,
            cancellationToken: ct);
        return result.ModifiedCount;
    }
}
