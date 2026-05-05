using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public sealed class Koordinat
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public sealed class Magaza
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string FirmaId { get; set; } = string.Empty;

    public string Ad { get; set; } = string.Empty;
    public string Sehir { get; set; } = string.Empty;
    public string Ilce { get; set; } = string.Empty;
    public string Adres { get; set; } = string.Empty;
    public Koordinat? Koordinat { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? MuduruKullaniciId { get; set; }

    public bool AktifMi { get; set; } = true;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellenmeTarihi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OlusturanKullaniciId { get; set; }
}
