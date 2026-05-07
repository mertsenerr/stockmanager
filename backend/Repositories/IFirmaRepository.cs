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
    /// <summary>
    /// Phase 3.1: owner-scoped uniqueness for client firma create/update. Two SayimBaskanis
    /// independently auditing the same real-world brand (e.g. "LCW") can each keep their
    /// own client firma record — duplicates only conflict within a single owner's catalog.
    /// Filters out organizasyon firmas so an owner's personal org firma doesn't clash with
    /// a client firma of the same name.
    /// </summary>
    Task<bool> AdExistsForOwnerAsync(string ad, string ownerUserId, string? excludeId, CancellationToken ct = default);
    Task<bool> KisaltmaExistsForOwnerAsync(string kisaltma, string ownerUserId, string? excludeId, CancellationToken ct = default);
    Task InsertAsync(Firma firma, CancellationToken ct = default);
    Task ReplaceAsync(Firma firma, CancellationToken ct = default);
    Task SoftDeleteAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Phase 2.5: returns active organizasyon Firmas the given user owns (created).
    /// Used to scope per-user visibility under the personal-tenancy model.
    /// </summary>
    Task<IReadOnlyList<Firma>> ListOwnedOrgFirmasByAsync(string ownerUserId, CancellationToken ct = default);
}

public sealed class FirmaRepository : IFirmaRepository
{
    private readonly IMongoCollection<Firma> _firmalar;

    public FirmaRepository(IMongoDbService mongo)
    {
        _firmalar = mongo.Database.GetCollection<Firma>("firmalar");

        // Phase 2.5: index management moved to Phase2_5MigrationHostedService.
        // Under personal tenancy, FirmaAdi/Kisaltma duplicates are allowed, so the
        // formerly-unique partial indexes are dropped and replaced with non-unique
        // versions in the migration. The repository constructor no longer touches
        // indexes — single source of truth lives in the hosted service.
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

    public async Task<IReadOnlyList<Firma>> ListOwnedOrgFirmasByAsync(string ownerUserId, CancellationToken ct = default)
    {
        var filter = Builders<Firma>.Filter.And(
            Builders<Firma>.Filter.Eq(f => f.OlusturanKullaniciId, ownerUserId),
            Builders<Firma>.Filter.Eq(f => f.OrganizasyonMu, true),
            Builders<Firma>.Filter.Eq(f => f.AktifMi, true));
        return await _firmalar.Find(filter).ToListAsync(ct);
    }

    public async Task<bool> AdExistsForOwnerAsync(
        string ad, string ownerUserId, string? excludeId, CancellationToken ct = default)
    {
        var filter = Builders<Firma>.Filter.Eq(f => f.Ad, ad)
                   & Builders<Firma>.Filter.Eq(f => f.AktifMi, true)
                   & Builders<Firma>.Filter.Eq(f => f.OlusturanKullaniciId, ownerUserId)
                   & Builders<Firma>.Filter.Ne(f => f.OrganizasyonMu, true);
        if (!string.IsNullOrEmpty(excludeId))
            filter &= Builders<Firma>.Filter.Ne(f => f.Id, excludeId);
        return await _firmalar.Find(filter).AnyAsync(ct);
    }

    public async Task<bool> KisaltmaExistsForOwnerAsync(
        string kisaltma, string ownerUserId, string? excludeId, CancellationToken ct = default)
    {
        var k = kisaltma.ToUpperInvariant();
        var filter = Builders<Firma>.Filter.Eq(f => f.Kisaltma, k)
                   & Builders<Firma>.Filter.Eq(f => f.AktifMi, true)
                   & Builders<Firma>.Filter.Eq(f => f.OlusturanKullaniciId, ownerUserId)
                   & Builders<Firma>.Filter.Ne(f => f.OrganizasyonMu, true);
        if (!string.IsNullOrEmpty(excludeId))
            filter &= Builders<Firma>.Filter.Ne(f => f.Id, excludeId);
        return await _firmalar.Find(filter).AnyAsync(ct);
    }
}
