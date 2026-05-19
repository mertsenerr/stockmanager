using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

[BsonIgnoreExtraElements]
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
    /// Yükleme anındaki imza alanları (BelgeTipi'nden snapshot — rol + konum).
    /// Katalog sonradan değişirse mevcut dosyalar etkilenmesin diye snapshot.
    /// Boş liste = imza gerekmiyor.
    /// </summary>
    public List<ImzaSlot> ImzaSlotlari { get; set; } = [];

    /// <summary>Yükleme anındaki kaşe gereksinimi snapshot'ı.</summary>
    public bool KaseGerekli { get; set; }

    /// <summary>Yükleme anındaki kaşe konumu snapshot'ı (ImzaKonumlari sabitlerinden).</summary>
    public string? KaseKonum { get; set; }

    /// <summary>
    /// Bu dosyaya atılan imzaların listesi. Her rol için en fazla bir kayıt
    /// olur (DELETE ile geri alınabilir, sonra yeniden eklenebilir).
    /// </summary>
    public List<DosyaImza> Imzalar { get; set; } = [];

    /// <summary>
    /// Kaşe damgası — atıldığında doludur. KaseGerekli=true olan dosyalar
    /// için Mağaza Yetkilisi (veya Sistem) tarafından basılır.
    /// </summary>
    public KaseDamga? Kase { get; set; }
}

/// <summary>
/// Tek bir imza kaydı — bir dosyaya, belirli bir rolün, belirli bir kullanıcı
/// tarafından atıldığı imza. UX mock için imza görseli PNG data URI olarak
/// saklanır (gerçek e-imza değil, sadece görsel rüya).
/// </summary>
public sealed class DosyaImza
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Hangi rolün imza slotu (ImzaRolleri sabitlerinden).</summary>
    public string Rol { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = string.Empty;

    public string KullaniciAdSoyad { get; set; } = string.Empty;

    /// <summary>Canvas'tan üretilmiş PNG (data:image/png;base64,...) — render aşamasında PDF'e bindirilir.</summary>
    public string ImzaGorseliDataUri { get; set; } = string.Empty;

    public DateTime ImzalanmaTarihi { get; set; } = DateTime.UtcNow;
}

public sealed class KaseDamga
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string BasanKullaniciId { get; set; } = string.Empty;

    public string BasanAdSoyad { get; set; } = string.Empty;

    public DateTime Tarih { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
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
