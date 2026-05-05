using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IFirmaRepository
{
    /// <summary>
    /// Müşteri firmaları listeler (organizasyon firmaları varsayılan olarak hariç).
    /// </summary>
    Task<IReadOnlyList<Firma>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<Firma?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<Firma?> FindByKisaltmaAsync(string kisaltma, CancellationToken ct = default);
    Task<bool> AdExistsAsync(string ad, string? excludeId, CancellationToken ct = default);
    Task<bool> KisaltmaExistsAsync(string kisaltma, string? excludeId, CancellationToken ct = default);
    Task InsertAsync(Firma firma, CancellationToken ct = default);
    Task ReplaceAsync(Firma firma, CancellationToken ct = default);
    Task SoftDeleteAsync(string id, CancellationToken ct = default);
}

public sealed class FirmaRepository : IFirmaRepository
{
    private readonly IMongoCollection<Firma> _firmalar;

    public FirmaRepository(IMongoDbService mongo)
    {
        _firmalar = mongo.Database.GetCollection<Firma>("firmalar");

        // Eski hard-unique indexleri drop et (varsa) — silinmiş kayıtlar yeni kayıtla çakışıyordu.
        TryDropIndex("ix_firmalar_ad_unique");
        TryDropIndex("ix_firmalar_kisaltma_unique");

        // Yeni partial unique indexler — sadece aktif kayıtlar arasında benzersizlik.
        var activeFilter = Builders<Firma>.Filter.Eq(f => f.AktifMi, true);

        try
        {
            _firmalar.Indexes.CreateOne(new CreateIndexModel<Firma>(
                Builders<Firma>.IndexKeys.Ascending(f => f.Ad),
                new CreateIndexOptions<Firma>
                {
                    Unique = true,
                    Name = "ix_firmalar_ad_unique_active",
                    PartialFilterExpression = activeFilter,
                }));
        }
        catch { /* mevcut çakışan veride bile servis ayağa kalksın */ }

        try
        {
            _firmalar.Indexes.CreateOne(new CreateIndexModel<Firma>(
                Builders<Firma>.IndexKeys.Ascending(f => f.Kisaltma),
                new CreateIndexOptions<Firma>
                {
                    Unique = true,
                    Name = "ix_firmalar_kisaltma_unique_active",
                    PartialFilterExpression = Builders<Firma>.Filter.And(
                        activeFilter,
                        Builders<Firma>.Filter.Ne(f => f.Kisaltma, string.Empty)),
                }));
        }
        catch { /* mevcut çakışan veride bile servis ayağa kalksın */ }
    }

    private void TryDropIndex(string name)
    {
        try { _firmalar.Indexes.DropOne(name); }
        catch { /* yoksa yoksay */ }
    }

    public Task<Firma?> FindByKisaltmaAsync(string kisaltma, CancellationToken ct = default) =>
        _firmalar.Find(f => f.Kisaltma == kisaltma.ToUpperInvariant() && f.AktifMi).FirstOrDefaultAsync(ct)!;

    public async Task<bool> KisaltmaExistsAsync(string kisaltma, string? excludeId, CancellationToken ct = default)
    {
        var k = kisaltma.ToUpperInvariant();
        // Sadece aktif kayıtları say — eski soft-deleted kısaltmalar yeniden kullanımı engellemesin.
        var filter = Builders<Firma>.Filter.Eq(f => f.Kisaltma, k)
                   & Builders<Firma>.Filter.Eq(f => f.AktifMi, true);
        if (!string.IsNullOrEmpty(excludeId))
            filter &= Builders<Firma>.Filter.Ne(f => f.Id, excludeId);
        return await _firmalar.Find(filter).AnyAsync(ct);
    }

    public async Task<IReadOnlyList<Firma>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var filter = Builders<Firma>.Filter.Ne(f => f.OrganizasyonMu, true);
        if (!includeInactive)
            filter &= Builders<Firma>.Filter.Eq(f => f.AktifMi, true);
        return await _firmalar.Find(filter).SortBy(f => f.Ad).ToListAsync(ct);
    }

    public Task<Firma?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _firmalar.Find(f => f.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task<bool> AdExistsAsync(string ad, string? excludeId, CancellationToken ct = default)
    {
        // Sadece aktif kayıtları say — geçmişteki soft-deleted (AktifMi=false) kayıtlar yeniden açmayı engellemesin.
        var filter = Builders<Firma>.Filter.Eq(f => f.Ad, ad)
                   & Builders<Firma>.Filter.Eq(f => f.AktifMi, true);
        if (!string.IsNullOrEmpty(excludeId))
            filter &= Builders<Firma>.Filter.Ne(f => f.Id, excludeId);
        return await _firmalar.Find(filter).AnyAsync(ct);
    }

    public Task InsertAsync(Firma firma, CancellationToken ct = default) =>
        _firmalar.InsertOneAsync(firma, cancellationToken: ct);

    public Task ReplaceAsync(Firma firma, CancellationToken ct = default)
    {
        firma.GuncellenmeTarihi = DateTime.UtcNow;
        return _firmalar.ReplaceOneAsync(f => f.Id == firma.Id, firma, cancellationToken: ct);
    }

    public async Task SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        // Firma silme akışı kalıcıdır: aynı ad/kısaltmayla yeniden oluşturulabilsin diye DB'den tamamen kaldırılır.
        // Geçmiş sayım oturumları FirmaId referansı taşır; organizasyon firmaları (OrganizasyonMu=true) bu listeye gelmez.
        await _firmalar.DeleteOneAsync(f => f.Id == id, ct);
    }
}
