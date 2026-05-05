using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SayimLink.Api.Models;

public static class OturumDurumlari
{
    public const string Taslak = "taslak";
    public const string ExcelBekleniyor = "excel_bekleniyor";
    public const string Aktif = "aktif";
    public const string Kilitli = "kilitli";
    public const string Tamamlandi = "tamamlandi";
    public const string Iptal = "iptal";

    public static readonly IReadOnlyCollection<string> All = new[]
    { Taslak, ExcelBekleniyor, Aktif, Kilitli, Tamamlandi, Iptal };

    public static bool IsValid(string d) => All.Contains(d);
}

public static class UrunDurumlari
{
    public const string Beklemede = "beklemede";
    public const string TekrarSayiliyor = "tekrar_sayiliyor";
    public const string Onaylandi = "onaylandi";
    public const string Iptal = "iptal";
    public const string Incele = "incele";

    public static readonly IReadOnlyCollection<string> All = new[]
    { Beklemede, TekrarSayiliyor, Onaylandi, Iptal, Incele };

    public static bool IsValid(string d) => All.Contains(d);
}

public sealed class Katilimci
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public DateTime KatilmaTarihi { get; set; } = DateTime.UtcNow;
    public bool AktifMi { get; set; } = true;
}

public sealed class ExcelMapping
{
    public string? BarkodKolon { get; set; }
    public string? UrunAdiKolon { get; set; }
    public string? SistemStokKolon { get; set; }
    public string? SayilanStokKolon { get; set; }
    public string? StokKoduKolon { get; set; }
    public string? KategoriKolon { get; set; }
    public string? AltKategoriKolon { get; set; }
    public string? RenkKolon { get; set; }
    public string? BedenKolon { get; set; }
    public string? MarkaKolon { get; set; }
    public string? ModelKolon { get; set; }
    public string? FiyatKolon { get; set; }
}

public sealed class UrunYorum
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;
    public string Mesaj { get; set; } = string.Empty;
    public DateTime Tarih { get; set; } = DateTime.UtcNow;
}

public sealed class UrunDegisiklik
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;
    public string Alan { get; set; } = string.Empty;
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }
    public DateTime Tarih { get; set; } = DateTime.UtcNow;
}

public static class TalepDurumlari
{
    public const string Beklemede = "beklemede";
    public const string Onaylandi = "onaylandi";
    public const string Reddedildi = "reddedildi";

    public static readonly IReadOnlyCollection<string> All = new[]
    { Beklemede, Onaylandi, Reddedildi };

    public static bool IsValid(string d) => All.Contains(d);
}

public sealed class UrunDegisiklikTalebi
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;

    /// <summary>Şimdilik sadece "sayilanStok"; ileride genişletilebilir.</summary>
    public string Alan { get; set; } = string.Empty;
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }
    public string? Gerekce { get; set; }

    public string Durum { get; set; } = TalepDurumlari.Beklemede;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KararVerenId { get; set; }
    public string? KararVerenAdi { get; set; }
    public string? KararSebep { get; set; }
    public DateTime? KararTarihi { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}

public sealed class OturumUrun
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Barkod { get; set; } = string.Empty;
    public string UrunAdi { get; set; } = string.Empty;
    public decimal SistemStok { get; set; }
    public decimal SayilanStok { get; set; }
    public decimal Fark => SayilanStok - SistemStok;

    // Tip bazlı opsiyonel alanlar — Excel'den tip profiline göre doldurulur.
    public string? StokKodu { get; set; }
    public string? Kategori { get; set; }
    public string? AltKategori { get; set; }
    public string? Renk { get; set; }
    public string? Beden { get; set; }
    public string? Marka { get; set; }
    public string? Model { get; set; }
    public decimal? Fiyat { get; set; }

    public string Durum { get; set; } = UrunDurumlari.Beklemede;
    public List<UrunYorum> Yorumlar { get; set; } = [];
    public List<UrunDegisiklik> DegisiklikGecmisi { get; set; } = [];
    public List<UrunDegisiklikTalebi> Talepler { get; set; } = [];

    [BsonRepresentation(BsonType.ObjectId)]
    public string? AtananSaymanId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KilitleyenKullaniciId { get; set; }
    public DateTime? KilitlenmeTarihi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? SonGuncelleyenId { get; set; }
    public DateTime? GuncellenmeTarihi { get; set; }
}

public sealed class OturumOzet
{
    public int ToplamUrun { get; set; }
    public int BeklemedeSayisi { get; set; }
    public int TekrarSayilan { get; set; }
    public int Onaylanmis { get; set; }
    public int IptalEdilmis { get; set; }
    public int Inceleme { get; set; }
    public decimal ToplamFarkPozitif { get; set; }
    public decimal ToplamFarkNegatif { get; set; }
}

public sealed class SayimOturumu
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string MagazaId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string FirmaId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? AtamaId { get; set; }

    public DateTime Tarih { get; set; }
    public string Durum { get; set; } = OturumDurumlari.Taslak;

    public List<Katilimci> Katilimcilar { get; set; } = [];
    /// <summary>E-posta ile davet edilen kullanıcılar — kayıt olduktan sonra otomatik erişim alır.</summary>
    public List<string> DavetliMailler { get; set; } = [];
    public ExcelMapping ExcelMapping { get; set; } = new();
    public List<OturumUrun> Urunler { get; set; } = [];
    public OturumOzet Ozetler { get; set; } = new();

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? BaslangicTarihi { get; set; }
    public DateTime? BitisTarihi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string OlusturanId { get; set; } = string.Empty;

    public static OturumOzet ComputeOzet(IEnumerable<OturumUrun> urunler)
    {
        var oz = new OturumOzet();
        foreach (var u in urunler)
        {
            oz.ToplamUrun++;
            switch (u.Durum)
            {
                case UrunDurumlari.Beklemede: oz.BeklemedeSayisi++; break;
                case UrunDurumlari.TekrarSayiliyor: oz.TekrarSayilan++; break;
                case UrunDurumlari.Onaylandi: oz.Onaylanmis++; break;
                case UrunDurumlari.Iptal: oz.IptalEdilmis++; break;
                case UrunDurumlari.Incele: oz.Inceleme++; break;
            }
            var fark = u.SayilanStok - u.SistemStok;
            if (fark > 0) oz.ToplamFarkPozitif += fark;
            else if (fark < 0) oz.ToplamFarkNegatif += fark;
        }
        return oz;
    }
}
