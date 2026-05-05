using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public static class AtamaDurumlari
{
    public const string Planlandi = "planlandi";
    public const string Tamamlandi = "tamamlandi";
    public const string Iptal = "iptal";

    public static readonly IReadOnlyCollection<string> All =
        new[] { Planlandi, Tamamlandi, Iptal };

    public static bool IsValid(string durum) => All.Contains(durum);
}

public sealed class Atama
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string MagazaId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string FirmaId { get; set; } = string.Empty;

    /// <summary>UTC midnight of the assignment date (no time component).</summary>
    public DateTime Tarih { get; set; }

    /// <summary>Optional clock-time start, format HH:mm (e.g. "09:30").</summary>
    public string? BaslangicSaati { get; set; }
    public string? BitisSaati { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string YoneticiKullaniciId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> SaymanKullaniciIds { get; set; } = [];

    public string? Notlar { get; set; }

    public string Durum { get; set; } = AtamaDurumlari.Planlandi;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellenmeTarihi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OlusturanKullaniciId { get; set; }
}
