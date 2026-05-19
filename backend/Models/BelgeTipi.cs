using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

/// <summary>
/// Bir firmanın belge kataloğundaki tek bir belge tipini temsil eder
/// (örn. "Sayım Kabul Tutanağı"). Özel Rapor dosyaları yüklenirken bu
/// kataloğun bir öğesine bağlanır; o anki imza/kaşe gereksinimleri
/// dosyaya snapshot olarak yazılır.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class BelgeTipi
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Katalog firma bazında izole — her firmanın kendi listesi.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string FirmaId { get; set; } = string.Empty;

    public string Ad { get; set; } = string.Empty;

    public string? Aciklama { get; set; }

    /// <summary>
    /// Bu belgeye imza atması gereken rollerin ve PDF üzerinde nereye
    /// yerleşeceklerinin listesi. Boş liste = imza gerekmiyor.
    /// </summary>
    public List<ImzaSlot> ImzaSlotlari { get; set; } = [];

    public bool KaseGerekli { get; set; }

    /// <summary>Kaşe gerektiğinde PDF'te nereye basılacak (ImzaKonumlari sabitlerinden).</summary>
    public string? KaseKonum { get; set; }

    /// <summary>Soft delete bayrağı; arşivlenen kayıtlar listelerden gizlenir.</summary>
    public bool Arsivlendi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OlusturanKullaniciId { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellenmeTarihi { get; set; }
}

/// <summary>
/// Bir belgeye imza atabilecek rol sabitleri. String tabanlı — yeni rol
/// eklemek için sadece bu listeye sabit eklemek yeterli (migration gerekmiyor).
/// </summary>
public static class ImzaRolleri
{
    public const string SayimBaskani = "SayimBaskani";
    public const string MagazaYetkilisi = "MagazaYetkilisi";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        SayimBaskani, MagazaYetkilisi,
    };

    public static bool IsValid(string rol) => All.Contains(rol);
}

/// <summary>
/// PDF üstünde imza/kaşe yerleşim konumu. Üç köşe — sayfanın altında, sol /
/// orta / sağ. Şimdilik basit; ileride pixel-level template editör'e geçilebilir.
/// </summary>
public static class ImzaKonumlari
{
    public const string SolAlt = "SolAlt";
    public const string OrtaAlt = "OrtaAlt";
    public const string SagAlt = "SagAlt";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        SolAlt, OrtaAlt, SagAlt,
    };

    public static bool IsValid(string konum) => All.Contains(konum);
}

/// <summary>
/// Tek bir imza alanı — hangi rol, PDF'te neresi. Belge tipi tanımında
/// kullanılır ve dosya yüklenirken snapshot'lanır.
/// </summary>
public sealed class ImzaSlot
{
    public string Rol { get; set; } = string.Empty;
    public string Konum { get; set; } = ImzaKonumlari.OrtaAlt;
}
