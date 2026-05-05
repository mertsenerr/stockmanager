using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IAtamaRepository
{
    Task<IReadOnlyList<Atama>> ListByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<Atama>> ListForUserAsync(string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<Atama?> FindByIdAsync(string id, CancellationToken ct = default);
    Task InsertAsync(Atama atama, CancellationToken ct = default);
    Task ReplaceAsync(Atama atama, CancellationToken ct = default);
    Task UpdateDateAsync(string id, DateTime newDateUtc, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class AtamaRepository : IAtamaRepository
{
    private readonly IMongoCollection<Atama> _atamalar;

    public AtamaRepository(IMongoDbService mongo)
    {
        _atamalar = mongo.Database.GetCollection<Atama>("atamalar");
        _atamalar.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<Atama>(
                Builders<Atama>.IndexKeys.Ascending(a => a.Tarih),
                new CreateIndexOptions { Name = "ix_atamalar_tarih" }),
            new CreateIndexModel<Atama>(
                Builders<Atama>.IndexKeys.Ascending(a => a.MagazaId).Ascending(a => a.Tarih),
                new CreateIndexOptions { Name = "ix_atamalar_magaza_tarih" }),
            new CreateIndexModel<Atama>(
                Builders<Atama>.IndexKeys.Ascending(a => a.YoneticiKullaniciId),
                new CreateIndexOptions { Name = "ix_atamalar_yonetici" }),
            new CreateIndexModel<Atama>(
                Builders<Atama>.IndexKeys.Ascending(a => a.SaymanKullaniciIds),
                new CreateIndexOptions { Name = "ix_atamalar_saymanlar" }),
        });
    }

    public async Task<IReadOnlyList<Atama>> ListByDateRangeAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await _atamalar.Find(a => a.Tarih >= fromUtc && a.Tarih < toUtc)
            .SortBy(a => a.Tarih).ToListAsync(ct);

    public async Task<IReadOnlyList<Atama>> ListForUserAsync(
        string userId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var filter = Builders<Atama>.Filter.And(
            Builders<Atama>.Filter.Gte(a => a.Tarih, fromUtc),
            Builders<Atama>.Filter.Lt(a => a.Tarih, toUtc),
            Builders<Atama>.Filter.Or(
                Builders<Atama>.Filter.Eq(a => a.YoneticiKullaniciId, userId),
                Builders<Atama>.Filter.AnyEq(a => a.SaymanKullaniciIds, userId)));
        return await _atamalar.Find(filter).SortBy(a => a.Tarih).ToListAsync(ct);
    }

    public Task<Atama?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _atamalar.Find(a => a.Id == id).FirstOrDefaultAsync(ct)!;

    public Task InsertAsync(Atama atama, CancellationToken ct = default) =>
        _atamalar.InsertOneAsync(atama, cancellationToken: ct);

    public Task ReplaceAsync(Atama atama, CancellationToken ct = default)
    {
        atama.GuncellenmeTarihi = DateTime.UtcNow;
        return _atamalar.ReplaceOneAsync(a => a.Id == atama.Id, atama, cancellationToken: ct);
    }

    public async Task UpdateDateAsync(string id, DateTime newDateUtc, CancellationToken ct = default)
    {
        var update = Builders<Atama>.Update
            .Set(a => a.Tarih, newDateUtc)
            .Set(a => a.GuncellenmeTarihi, DateTime.UtcNow);
        await _atamalar.UpdateOneAsync(a => a.Id == id, update, cancellationToken: ct);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        _atamalar.DeleteOneAsync(a => a.Id == id, ct);
}
