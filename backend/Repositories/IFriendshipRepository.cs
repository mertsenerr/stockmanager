using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IFriendshipRepository
{
    Task<Friendship?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<Friendship?> FindBetweenAsync(string userA, string userB, CancellationToken ct = default);
    Task<IReadOnlyList<Friendship>> ListAcceptedForUserAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<Friendship>> ListIncomingPendingAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyList<Friendship>> ListOutgoingPendingAsync(string userId, CancellationToken ct = default);
    Task InsertAsync(Friendship f, CancellationToken ct = default);
    Task ReplaceAsync(Friendship f, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly IMongoCollection<Friendship> _col;

    public FriendshipRepository(IMongoDbService mongo)
    {
        _col = mongo.Database.GetCollection<Friendship>("friendships");
        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<Friendship>(
                Builders<Friendship>.IndexKeys
                    .Ascending(f => f.FromUserId)
                    .Ascending(f => f.ToUserId),
                new CreateIndexOptions { Name = "ix_friendship_pair", Unique = true }));
        }
        catch { /* mevcut index uyumsuzluğunda servis ayağa kalksın */ }
    }

    public Task<Friendship?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _col.Find(f => f.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<Friendship?> FindBetweenAsync(string a, string b, CancellationToken ct = default) =>
        _col.Find(f =>
            (f.FromUserId == a && f.ToUserId == b) ||
            (f.FromUserId == b && f.ToUserId == a))
            .FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<Friendship>> ListAcceptedForUserAsync(string userId, CancellationToken ct = default)
    {
        var filter = Builders<Friendship>.Filter.And(
            Builders<Friendship>.Filter.Eq(f => f.Durum, FriendshipDurumlari.Kabul),
            Builders<Friendship>.Filter.Or(
                Builders<Friendship>.Filter.Eq(f => f.FromUserId, userId),
                Builders<Friendship>.Filter.Eq(f => f.ToUserId, userId)));
        return await _col.Find(filter).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Friendship>> ListIncomingPendingAsync(string userId, CancellationToken ct = default) =>
        await _col.Find(f => f.ToUserId == userId && f.Durum == FriendshipDurumlari.Beklemede).ToListAsync(ct);

    public async Task<IReadOnlyList<Friendship>> ListOutgoingPendingAsync(string userId, CancellationToken ct = default) =>
        await _col.Find(f => f.FromUserId == userId && f.Durum == FriendshipDurumlari.Beklemede).ToListAsync(ct);

    public Task InsertAsync(Friendship f, CancellationToken ct = default) =>
        _col.InsertOneAsync(f, cancellationToken: ct);

    public Task ReplaceAsync(Friendship f, CancellationToken ct = default) =>
        _col.ReplaceOneAsync(x => x.Id == f.Id, f, cancellationToken: ct);

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        _col.DeleteOneAsync(f => f.Id == id, ct);
}
