using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public sealed class OzelRaporDosya
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Kullanıcının yüklediği orijinal dosya adı.</summary>
    public string Ad { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long Boyut { get; set; }

    /// <summary>Disk'te raporun klasörü içindeki saklanma adı (genelde {Id}{ext}).</summary>
    public string StorageName { get; set; } = string.Empty;

    public DateTime YuklemeTarihi { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bağlı olduğu BelgeTipi (firma katalogundan). Null = sınıflandırılmamış.
    /// </summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string? BelgeTipiId { get; set; }

    /// <summary>
    /// Yükleme anındaki imza gerekleri (BelgeTipi'nden snapshot). Katalog
    /// sonradan değişirse mevcut dosyalar etkilenmesin diye snapshot kullanıyoruz.
    /// Boş liste = imza gerekmiyor.
    /// </summary>
    public List<string> ImzaGerekenRoller { get; set; } = [];

    /// <summary>Yükleme anındaki kaşe gereksinimi snapshot'ı.</summary>
    public bool KaseGerekli { get; set; }
}

public sealed class OzelRapor
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Ad { get; set; } = string.Empty;

    public string? Aciklama { get; set; }

    /// <summary>
    /// Raporu oluşturan SayimBaskani'nın id'si. İzolasyonun temeli — başka SayimBaskani
    /// (aynı firmadakiler dahil) bu raporu göremez.
    /// </summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string OlusturanKullaniciId { get; set; } = string.Empty;

    /// <summary>Erişim verilen kullanıcı id'leri. Sadece bu listede olanlar (ve oluşturan/Sistem) raporu görür.</summary>
    public List<string> ErisebilenKullaniciIds { get; set; } = [];

    public List<OzelRaporDosya> Dosyalar { get; set; } = [];

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellenmeTarihi { get; set; }
}
