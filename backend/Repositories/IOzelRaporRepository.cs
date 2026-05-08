using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IOzelRaporRepository
{
    Task<OzelRapor?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<OzelRapor>> ListAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OzelRapor>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<OzelRapor>> ListAccessibleByAsync(string userId, CancellationToken ct = default);
    Task InsertAsync(OzelRapor rapor, CancellationToken ct = default);
    Task ReplaceAsync(OzelRapor rapor, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class OzelRaporRepository : IOzelRaporRepository
{
    private readonly IMongoCollection<OzelRapor> _col;

    public OzelRaporRepository(IMongoDbService mongo)
    {
        _col = mongo.Database.GetCollection<OzelRapor>("ozel_raporlar");

        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<OzelRapor>(
                Builders<OzelRapor>.IndexKeys.Ascending(r => r.OlusturanKullaniciId),
                new CreateIndexOptions { Name = "ix_ozelraporlar_owner" }));
            _col.Indexes.CreateOne(new CreateIndexModel<OzelRapor>(
                Builders<OzelRapor>.IndexKeys.Ascending(r => r.ErisebilenKullaniciIds),
                new CreateIndexOptions { Name = "ix_ozelraporlar_access" }));
        }
        catch { /* yoksay */ }
    }

    public Task<OzelRapor?> FindByIdAsync(string id, CancellationToken ct = default) =>
        _col.Find(r => r.Id == id).FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<OzelRapor>> ListAllAsync(CancellationToken ct = default) =>
        await _col.Find(Builders<OzelRapor>.Filter.Empty)
            .SortByDescending(r => r.OlusturmaTarihi).ToListAsync(ct);

    public async Task<IReadOnlyList<OzelRapor>> ListByOwnerAsync(string ownerUserId, CancellationToken ct = default) =>
        await _col.Find(r => r.OlusturanKullaniciId == ownerUserId)
            .SortByDescending(r => r.OlusturmaTarihi).ToListAsync(ct);

    public async Task<IReadOnlyList<OzelRapor>> ListAccessibleByAsync(string userId, CancellationToken ct = default)
    {
        var filter = Builders<OzelRapor>.Filter.AnyEq(r => r.ErisebilenKullaniciIds, userId);
        return await _col.Find(filter).SortByDescending(r => r.OlusturmaTarihi).ToListAsync(ct);
    }

    public Task InsertAsync(OzelRapor rapor, CancellationToken ct = default) =>
        _col.InsertOneAsync(rapor, cancellationToken: ct);

    public Task ReplaceAsync(OzelRapor rapor, CancellationToken ct = default)
    {
        rapor.GuncellenmeTarihi = DateTime.UtcNow;
        return _col.ReplaceOneAsync(r => r.Id == rapor.Id, rapor, cancellationToken: ct);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) =>
        _col.DeleteOneAsync(r => r.Id == id, ct);
}
