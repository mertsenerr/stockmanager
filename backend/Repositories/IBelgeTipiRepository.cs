using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IBelgeTipiRepository
{
    Task<BelgeTipi?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<BelgeTipi>> ListByFirmaAsync(string firmaId, bool includeArchived, CancellationToken ct = default);
    Task<IReadOnlyList<BelgeTipi>> ListAllAsync(bool includeArchived, CancellationToken ct = default);
    Task<bool> ExistsByAdAsync(string firmaId, string ad, string? excludeId, CancellationToken ct = default);
    Task InsertAsync(BelgeTipi belgeTipi, CancellationToken ct = default);
    Task ReplaceAsync(BelgeTipi belgeTipi, CancellationToken ct = default);
}

public sealed class BelgeTipiRepository : IBelgeTipiRepository
{
    private readonly IMongoCollection<BelgeTipi> _col;

    public BelgeTipiRepository(IMongoDbService mongo)
    {
        _col = mongo.Database.GetCollection<BelgeTipi>("belge_tipleri");

        try
        {
            // (FirmaId, Ad) çifti üzerinde unique partial index — sadece arşivlenmemiş
            // kayıtlar için. Bir firma içinde aynı isimde iki aktif belge tipi olmasın,
            // ama arşivlenenler bu kısıttan muaf (eskiyi arşivleyip yenisine aynı ismi
            // vermek mümkün olsun).
            _col.Indexes.CreateOne(new CreateIndexModel<BelgeTipi>(
                Builders<BelgeTipi>.IndexKeys
                    .Ascending(t => t.FirmaId)
                    .Ascending(t => t.Ad),
                new CreateIndexOptions<BelgeTipi>
                {
                    Name = "ix_belgetipleri_firma_ad_unique",
                    Unique = true,
                    PartialFilterExpression = Builders<BelgeTipi>.Filter.Eq(t => t.Arsivlendi, false),
                }));
            _col.Indexes.CreateOne(new CreateIndexModel<BelgeTipi>(
                Builders<BelgeTipi>.IndexKeys.Ascending(t => t.FirmaId),
                new CreateIndexOptions { Name = "ix_belgetipleri_firma" }));
        }
        catch { /* yoksay */ }
    }

    public Task<BelgeTipi?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _col.Find(t => t.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<BelgeTipi>> ListByFirmaAsync(
        string firmaId, bool includeArchived, CancellationToken ct = default)
    {
        var filter = includeArchived
            ? Builders<BelgeTipi>.Filter.Eq(t => t.FirmaId, firmaId)
            : Builders<BelgeTipi>.Filter.And(
                Builders<BelgeTipi>.Filter.Eq(t => t.FirmaId, firmaId),
                Builders<BelgeTipi>.Filter.Eq(t => t.Arsivlendi, false));
        return await _col.Find(filter).SortBy(t => t.Ad).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BelgeTipi>> ListAllAsync(bool includeArchived, CancellationToken ct = default)
    {
        var filter = includeArchived
            ? Builders<BelgeTipi>.Filter.Empty
            : Builders<BelgeTipi>.Filter.Eq(t => t.Arsivlendi, false);
        return await _col.Find(filter).SortBy(t => t.FirmaId).ThenBy(t => t.Ad).ToListAsync(ct);
    }

    public Task<bool> ExistsByAdAsync(string firmaId, string ad, string? excludeId, CancellationToken ct = default)
    {
        var filter = Builders<BelgeTipi>.Filter.And(
            Builders<BelgeTipi>.Filter.Eq(t => t.FirmaId, firmaId),
            Builders<BelgeTipi>.Filter.Eq(t => t.Ad, ad),
            Builders<BelgeTipi>.Filter.Eq(t => t.Arsivlendi, false));
        if (!string.IsNullOrEmpty(excludeId))
            filter = Builders<BelgeTipi>.Filter.And(filter, Builders<BelgeTipi>.Filter.Ne(t => t.Id, excludeId));
        return _col.Find(filter).AnyAsync(ct);
    }

    public Task InsertAsync(BelgeTipi belgeTipi, CancellationToken ct = default) =>
        _col.InsertOneAsync(belgeTipi, cancellationToken: ct);

    public Task ReplaceAsync(BelgeTipi belgeTipi, CancellationToken ct = default)
    {
        belgeTipi.GuncellenmeTarihi = DateTime.UtcNow;
        return _col.ReplaceOneAsync(t => t.Id == belgeTipi.Id, belgeTipi, cancellationToken: ct);
    }
}
