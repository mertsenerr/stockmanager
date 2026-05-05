using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IOturumRepository
{
    Task<IReadOnlyList<SayimOturumu>> ListAsync(
        string? magazaId, string? durum, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<SayimOturumu>> ListByMagazaIdsAsync(IEnumerable<string> magazaIds, CancellationToken ct = default);
    Task<IReadOnlyList<SayimOturumu>> ListWhereUserParticipatesAsync(string userId, CancellationToken ct = default);
    Task<SayimOturumu?> FindByIdAsync(string id, CancellationToken ct = default);
    Task InsertAsync(SayimOturumu oturum, CancellationToken ct = default);
    Task ReplaceAsync(SayimOturumu oturum, CancellationToken ct = default);
    Task UpdateDurumAsync(string id, string durum, CancellationToken ct = default);
    Task<SayimOturumu?> ReplaceUrunlerAndOzetAsync(
        string id, ExcelMapping mapping, IReadOnlyList<OturumUrun> urunler, OturumOzet ozet, CancellationToken ct = default);
    Task<UpdateResult> UpdateUrunAsync(
        string oturumId, string urunId, UpdateDefinition<SayimOturumu> update, CancellationToken ct = default);
    Task<bool> HardDeleteAsync(string id, CancellationToken ct = default);
}

public sealed class OturumRepository : IOturumRepository
{
    private readonly IMongoCollection<SayimOturumu> _oturumlar;

    public OturumRepository(IMongoDbService mongo)
    {
        _oturumlar = mongo.Database.GetCollection<SayimOturumu>("sayim_oturumlari");
        _oturumlar.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<SayimOturumu>(
                Builders<SayimOturumu>.IndexKeys.Ascending(o => o.MagazaId).Ascending(o => o.Tarih),
                new CreateIndexOptions { Name = "ix_oturum_magaza_tarih" }),
            new CreateIndexModel<SayimOturumu>(
                Builders<SayimOturumu>.IndexKeys.Ascending(o => o.Durum).Ascending(o => o.Tarih),
                new CreateIndexOptions { Name = "ix_oturum_durum_tarih" }),
            new CreateIndexModel<SayimOturumu>(
                Builders<SayimOturumu>.IndexKeys.Ascending("katilimcilar.kullaniciId"),
                new CreateIndexOptions { Name = "ix_oturum_katilimci" }),
        });
    }

    public IMongoCollection<SayimOturumu> Collection => _oturumlar;

    public async Task<IReadOnlyList<SayimOturumu>> ListAsync(
        string? magazaId, string? durum, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var filter = Builders<SayimOturumu>.Filter.Empty;
        if (!string.IsNullOrEmpty(magazaId))
            filter &= Builders<SayimOturumu>.Filter.Eq(o => o.MagazaId, magazaId);
        if (!string.IsNullOrEmpty(durum))
            filter &= Builders<SayimOturumu>.Filter.Eq(o => o.Durum, durum);
        if (fromUtc.HasValue)
            filter &= Builders<SayimOturumu>.Filter.Gte(o => o.Tarih, fromUtc.Value);
        if (toUtc.HasValue)
            filter &= Builders<SayimOturumu>.Filter.Lt(o => o.Tarih, toUtc.Value);

        return await _oturumlar
            .Find(filter)
            .Project<SayimOturumu>(SummaryProjection)
            .SortByDescending(o => o.Tarih)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SayimOturumu>> ListByMagazaIdsAsync(
        IEnumerable<string> magazaIds, CancellationToken ct = default)
    {
        var ids = magazaIds.ToList();
        if (ids.Count == 0) return Array.Empty<SayimOturumu>();
        return await _oturumlar
            .Find(Builders<SayimOturumu>.Filter.In(o => o.MagazaId, ids))
            .Project<SayimOturumu>(SummaryProjection)
            .SortByDescending(o => o.Tarih)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SayimOturumu>> ListWhereUserParticipatesAsync(
        string userId, CancellationToken ct = default) =>
        await _oturumlar
            .Find(Builders<SayimOturumu>.Filter.ElemMatch(
                o => o.Katilimcilar,
                Builders<Katilimci>.Filter.Eq(k => k.KullaniciId, userId)))
            .Project<SayimOturumu>(SummaryProjection)
            .SortByDescending(o => o.Tarih)
            .ToListAsync(ct);

    public Task<SayimOturumu?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _oturumlar.Find(o => o.Id == id).FirstOrDefaultAsync(ct)!;

    public Task InsertAsync(SayimOturumu oturum, CancellationToken ct = default) =>
        _oturumlar.InsertOneAsync(oturum, cancellationToken: ct);

    public Task ReplaceAsync(SayimOturumu oturum, CancellationToken ct = default) =>
        _oturumlar.ReplaceOneAsync(o => o.Id == oturum.Id, oturum, cancellationToken: ct);

    public async Task UpdateDurumAsync(string id, string durum, CancellationToken ct = default)
    {
        var update = Builders<SayimOturumu>.Update.Set(o => o.Durum, durum);
        if (durum == OturumDurumlari.Aktif)
            update = update.Set(o => o.BaslangicTarihi, DateTime.UtcNow);
        if (durum == OturumDurumlari.Tamamlandi || durum == OturumDurumlari.Iptal)
            update = update.Set(o => o.BitisTarihi, DateTime.UtcNow);
        await _oturumlar.UpdateOneAsync(o => o.Id == id, update, cancellationToken: ct);
    }

    public async Task<SayimOturumu?> ReplaceUrunlerAndOzetAsync(
        string id, ExcelMapping mapping, IReadOnlyList<OturumUrun> urunler, OturumOzet ozet, CancellationToken ct = default)
    {
        var update = Builders<SayimOturumu>.Update
            .Set(o => o.ExcelMapping, mapping)
            .Set(o => o.Urunler, urunler.ToList())
            .Set(o => o.Ozetler, ozet)
            .Set(o => o.Durum, OturumDurumlari.Aktif)
            .Set(o => o.BaslangicTarihi, DateTime.UtcNow);
        await _oturumlar.UpdateOneAsync(o => o.Id == id, update, cancellationToken: ct);
        return await FindByIdAsync(id, ct);
    }

    public Task<UpdateResult> UpdateUrunAsync(
        string oturumId, string urunId, UpdateDefinition<SayimOturumu> update, CancellationToken ct = default) =>
        _oturumlar.UpdateOneAsync(
            Builders<SayimOturumu>.Filter.And(
                Builders<SayimOturumu>.Filter.Eq(o => o.Id, oturumId),
                Builders<SayimOturumu>.Filter.ElemMatch(o => o.Urunler, u => u.Id == urunId)),
            update,
            cancellationToken: ct);

    public async Task<bool> HardDeleteAsync(string id, CancellationToken ct = default)
    {
        var result = await _oturumlar.DeleteOneAsync(o => o.Id == id, ct);
        return result.DeletedCount > 0;
    }

    /// <summary>List projections exclude the heavy Urunler array; full doc is loaded only in FindByIdAsync.</summary>
    private static readonly ProjectionDefinition<SayimOturumu, SayimOturumu> SummaryProjection =
        Builders<SayimOturumu>.Projection.Exclude(o => o.Urunler);
}
