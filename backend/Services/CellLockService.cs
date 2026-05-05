using System.Collections.Concurrent;

namespace SayimLink.Api.Services;

public interface ICellLockService
{
    /// <summary>Try to acquire a lock for (oturum, urun, alan). Returns the active lock (either newly granted or pre-existing).</summary>
    CellLock Acquire(string oturumId, string urunId, string alan, string userId, string userAd);
    void Release(string oturumId, string urunId, string alan, string userId);
    IReadOnlyList<CellLock> GetActiveLocksForOturum(string oturumId);
    IReadOnlyList<CellLock> SweepExpired();
}

public sealed record CellLock(
    string OturumId,
    string UrunId,
    string Alan,
    string KullaniciId,
    string KullaniciAdi,
    DateTime ExpiresAt);

public sealed class CellLockService : ICellLockService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, CellLock> _locks = new();

    private static string Key(string oturumId, string urunId, string alan) =>
        $"{oturumId}|{urunId}|{alan}";

    public CellLock Acquire(string oturumId, string urunId, string alan, string userId, string userAd)
    {
        var key = Key(oturumId, urunId, alan);
        var now = DateTime.UtcNow;
        var newLock = new CellLock(oturumId, urunId, alan, userId, userAd, now.Add(Ttl));

        return _locks.AddOrUpdate(
            key,
            _ => newLock,
            (_, existing) =>
            {
                if (existing.ExpiresAt <= now) return newLock;
                if (existing.KullaniciId == userId)
                    return existing with { ExpiresAt = now.Add(Ttl) };
                return existing; // someone else holds it
            });
    }

    public void Release(string oturumId, string urunId, string alan, string userId)
    {
        var key = Key(oturumId, urunId, alan);
        if (_locks.TryGetValue(key, out var existing) && existing.KullaniciId == userId)
            _locks.TryRemove(new KeyValuePair<string, CellLock>(key, existing));
    }

    public IReadOnlyList<CellLock> GetActiveLocksForOturum(string oturumId)
    {
        var now = DateTime.UtcNow;
        return _locks.Values
            .Where(l => l.OturumId == oturumId && l.ExpiresAt > now)
            .ToList();
    }

    public IReadOnlyList<CellLock> SweepExpired()
    {
        var now = DateTime.UtcNow;
        var expired = _locks.Where(kv => kv.Value.ExpiresAt <= now).ToList();
        foreach (var kv in expired)
            _locks.TryRemove(kv);
        return expired.Select(kv => kv.Value).ToList();
    }
}
