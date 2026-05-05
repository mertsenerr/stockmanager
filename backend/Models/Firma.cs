using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public static class FirmaTipleri
{
    public const string Tekstil = "tekstil";
    public const string Market = "market";
    public const string IstasyonMarket = "istasyon_market";
    public const string Kozmetik = "kozmetik";
    public const string Elektronik = "elektronik";
    public const string Mobilya = "mobilya";
    public const string YapiMarket = "yapi_market";
    public const string Eczane = "eczane";
    public const string Ayakkabi = "ayakkabi";
    public const string Otomotiv = "otomotiv";
    public const string Kirtasiye = "kirtasiye";
    public const string Diger = "diger";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Tekstil, Market, IstasyonMarket, Kozmetik, Elektronik, Mobilya,
        YapiMarket, Eczane, Ayakkabi, Otomotiv, Kirtasiye, Diger,
    };
    public static bool IsValid(string tip) => All.Contains(tip);
}

public sealed class Firma
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Ad { get; set; } = string.Empty;
    /// <summary>Firma katılım anahtarı — kullanıcı kaydında firmayı bulmak için kullanılır. 3-6 büyük harf, unique.</summary>
    public string Kisaltma { get; set; } = string.Empty;
    public string Tip { get; set; } = FirmaTipleri.Diger;
    public string? LogoUrl { get; set; }

    /// <summary>
    /// True: Sayım hizmeti veren organizasyonun kendi şirketi (Sayım Başkanı kayıtta oluşturur).
    /// False: Sayımı yapılan müşteri firma (LCW, BSH vb.).
    /// Müşteri firma listelerinde organizasyon firmaları gösterilmez.
    /// </summary>
    public bool OrganizasyonMu { get; set; } = false;

    public bool AktifMi { get; set; } = true;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? GuncellenmeTarihi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OlusturanKullaniciId { get; set; }
}
