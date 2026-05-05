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
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IMongoCollection<RefreshToken> _tokens;

    public RefreshTokenRepository(IMongoDbService mongo)
    {
        _tokens = mongo.Database.GetCollection<RefreshToken>("refresh_tokens");
        _tokens.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Unique = true, Name = "ix_refresh_hash_unique" }),
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.UserId),
                new CreateIndexOptions { Name = "ix_refresh_user" }),
            new CreateIndexModel<RefreshToken>(
                Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions
                {
                    Name = "ix_refresh_ttl",
                    ExpireAfter = TimeSpan.FromDays(60),
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
}
