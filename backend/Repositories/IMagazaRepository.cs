using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IMagazaRepository
{
    Task<IReadOnlyList<Magaza>> ListAsync(string? firmaId, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<Magaza>> ListByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);
    Task<Magaza?> FindByIdAsync(string id, CancellationToken ct = default);
    Task InsertAsync(Magaza magaza, CancellationToken ct = default);
    Task ReplaceAsync(Magaza magaza, CancellationToken ct = default);
    Task SoftDeleteAsync(string id, CancellationToken ct = default);
    Task<bool> AnyForFirmaAsync(string firmaId, CancellationToken ct = default);
}

public sealed class MagazaRepository : IMagazaRepository
{
    private readonly IMongoCollection<Magaza> _magazalar;

    public MagazaRepository(IMongoDbService mongo)
    {
        _magazalar = mongo.Database.GetCollection<Magaza>("magazalar");
        _magazalar.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<Magaza>(
                Builders<Magaza>.IndexKeys
                    .Ascending(m => m.FirmaId)
                    .Ascending(m => m.AktifMi),
                new CreateIndexOptions { Name = "ix_magazalar_firma_aktif" }),
            new CreateIndexModel<Magaza>(
                Builders<Magaza>.IndexKeys.Ascending(m => m.MuduruKullaniciId),
                new CreateIndexOptions { Name = "ix_magazalar_mudur" }),
        });
    }

    public async Task<IReadOnlyList<Magaza>> ListAsync(string? firmaId, bool includeInactive, CancellationToken ct = default)
    {
        var filter = Builders<Magaza>.Filter.Empty;
        if (!string.IsNullOrEmpty(firmaId))
            filter &= Builders<Magaza>.Filter.Eq(m => m.FirmaId, firmaId);
        if (!includeInactive)
            filter &= Builders<Magaza>.Filter.Eq(m => m.AktifMi, true);
        return await _magazalar.Find(filter).SortBy(m => m.Ad).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Magaza>> ListByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<Magaza>();
        return await _magazalar
            .Find(Builders<Magaza>.Filter.In(m => m.Id, idList))
            .ToListAsync(ct);
    }

    public Task<Magaza?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _magazalar.Find(m => m.Id == id).FirstOrDefaultAsync(ct)!;

    public Task InsertAsync(Magaza magaza, CancellationToken ct = default) =>
        _magazalar.InsertOneAsync(magaza, cancellationToken: ct);

    public Task ReplaceAsync(Magaza magaza, CancellationToken ct = default)
    {
        magaza.GuncellenmeTarihi = DateTime.UtcNow;
        return _magazalar.ReplaceOneAsync(m => m.Id == magaza.Id, magaza, cancellationToken: ct);
    }

    public async Task SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        // Mağaza silme akışı kalıcıdır: aynı isimde yeniden oluşturulabilsin diye DB'den tamamen kaldırılır.
        // Geçmiş sayım oturumları MagazaId referansı tutar; gerekirse adlar oturum DTO'sundan okunur.
        await _magazalar.DeleteOneAsync(m => m.Id == id, ct);
    }

    public Task<bool> AnyForFirmaAsync(string firmaId, CancellationToken ct = default) =>
        _magazalar.Find(m => m.FirmaId == firmaId && m.AktifMi).AnyAsync(ct);
}
