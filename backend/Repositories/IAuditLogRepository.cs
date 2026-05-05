using MongoDB.Driver;
using SayimLink.Api.Models;
using SayimLink.Api.Services;

namespace SayimLink.Api.Repositories;

public interface IAuditLogRepository
{
    Task InsertAsync(AuditLog log, CancellationToken ct = default);
    Task InsertManyAsync(IEnumerable<AuditLog> logs, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditLog> items, long total)> QueryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? kullaniciId, string? aksiyon,
        int skip, int take, CancellationToken ct = default);
}

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IMongoCollection<AuditLog> _logs;

    public AuditLogRepository(IMongoDbService mongo)
    {
        _logs = mongo.Database.GetCollection<AuditLog>("audit_logs");
        _logs.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Descending(l => l.Tarih),
                new CreateIndexOptions { Name = "ix_audit_tarih" }),
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Ascending(l => l.KullaniciId).Descending(l => l.Tarih),
                new CreateIndexOptions { Name = "ix_audit_kullanici_tarih" }),
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Ascending(l => l.Aksiyon).Descending(l => l.Tarih),
                new CreateIndexOptions { Name = "ix_audit_aksiyon_tarih" }),
            // 180-day TTL keeps the audit table from growing unbounded.
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Ascending(l => l.Tarih),
                new CreateIndexOptions
                {
                    Name = "ix_audit_ttl",
                    ExpireAfter = TimeSpan.FromDays(180),
                }),
        });
    }

    public Task InsertAsync(AuditLog log, CancellationToken ct = default) =>
        _logs.InsertOneAsync(log, cancellationToken: ct);

    public Task InsertManyAsync(IEnumerable<AuditLog> logs, CancellationToken ct = default)
    {
        var list = logs.ToList();
        return list.Count == 0 ? Task.CompletedTask : _logs.InsertManyAsync(list, cancellationToken: ct);
    }

    public async Task<(IReadOnlyList<AuditLog> items, long total)> QueryAsync(
        DateTime? fromUtc, DateTime? toUtc, string? kullaniciId, string? aksiyon,
        int skip, int take, CancellationToken ct = default)
    {
        var filter = Builders<AuditLog>.Filter.Empty;
        if (fromUtc.HasValue) filter &= Builders<AuditLog>.Filter.Gte(l => l.Tarih, fromUtc.Value);
        if (toUtc.HasValue) filter &= Builders<AuditLog>.Filter.Lt(l => l.Tarih, toUtc.Value);
        if (!string.IsNullOrEmpty(kullaniciId))
            filter &= Builders<AuditLog>.Filter.Eq(l => l.KullaniciId, kullaniciId);
        if (!string.IsNullOrEmpty(aksiyon))
            filter &= Builders<AuditLog>.Filter.Eq(l => l.Aksiyon, aksiyon);

        var total = await _logs.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _logs.Find(filter)
            .SortByDescending(l => l.Tarih)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);
        return (items, total);
    }
}
