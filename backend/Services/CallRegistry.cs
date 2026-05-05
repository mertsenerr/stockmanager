using System.Collections.Concurrent;

namespace SayimLink.Api.Services;

public interface ICallRegistry
{
    /// <summary>Bir kullanıcı oturum aramasına katılır. Mevcut katılımcı listesini geri döner (kendi hariç).</summary>
    IReadOnlyList<CallParticipant> Join(string oturumId, CallParticipant me);
    /// <summary>Connection bazlı çıkış. Kalan katılımcılara duyurmak için ayrılan participant'ı döner (yoksa null).</summary>
    CallParticipant? Leave(string connectionId);
    /// <summary>connectionId'den oturumId çözer (sinyal yönlendirmede kullanılır, zorunlu değil ama faydalı).</summary>
    string? OturumIdOf(string connectionId);
    /// <summary>Bir oturumdaki tüm katılımcılar.</summary>
    IReadOnlyList<CallParticipant> Participants(string oturumId);
}

public sealed record CallParticipant(
    string ConnectionId,
    string KullaniciId,
    string KullaniciAdi,
    string Rol);

public sealed class CallRegistry : ICallRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CallParticipant>> _byOturum = new();
    private readonly ConcurrentDictionary<string, string> _connToOturum = new();

    public IReadOnlyList<CallParticipant> Join(string oturumId, CallParticipant me)
    {
        var bag = _byOturum.GetOrAdd(oturumId, _ => new ConcurrentDictionary<string, CallParticipant>());
        var existing = bag.Values.Where(p => p.ConnectionId != me.ConnectionId).ToList();
        bag[me.ConnectionId] = me;
        _connToOturum[me.ConnectionId] = oturumId;
        return existing;
    }

    public CallParticipant? Leave(string connectionId)
    {
        if (!_connToOturum.TryRemove(connectionId, out var oturumId)) return null;
        if (!_byOturum.TryGetValue(oturumId, out var bag)) return null;
        bag.TryRemove(connectionId, out var p);
        if (bag.IsEmpty) _byOturum.TryRemove(oturumId, out _);
        return p;
    }

    public string? OturumIdOf(string connectionId)
        => _connToOturum.TryGetValue(connectionId, out var o) ? o : null;

    public IReadOnlyList<CallParticipant> Participants(string oturumId)
        => _byOturum.TryGetValue(oturumId, out var bag) ? bag.Values.ToList() : Array.Empty<CallParticipant>();
}
