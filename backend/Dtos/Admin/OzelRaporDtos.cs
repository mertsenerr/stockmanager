namespace SayimLink.Api.Dtos.Admin;

public sealed class OzelRaporDosyaDto
{
    public string Id { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long Boyut { get; set; }
    public DateTime YuklemeTarihi { get; set; }
    public string? BelgeTipiId { get; set; }
    public string? BelgeTipiAdi { get; set; }
    public IReadOnlyList<string> ImzaGerekenRoller { get; set; } = [];
    public bool KaseGerekli { get; set; }
    public IReadOnlyList<DosyaImzaDto> Imzalar { get; set; } = [];
    public KaseDamgaDto? Kase { get; set; }
}

public sealed class DosyaImzaDto
{
    public string Id { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdSoyad { get; set; } = string.Empty;
    public DateTime ImzalanmaTarihi { get; set; }
    // Görsel data URI'yi DTO'ya koymuyoruz — listede önemli değil, sadece /signed
    // çıktısında lazım. Liste payload'ını şişirmeyelim.
}

public sealed class KaseDamgaDto
{
    public string BasanKullaniciId { get; set; } = string.Empty;
    public string BasanAdSoyad { get; set; } = string.Empty;
    public DateTime Tarih { get; set; }
}

public sealed class ImzaEkleRequest
{
    public string Rol { get; set; } = string.Empty;
    /// <summary>data:image/png;base64,... formatında PNG görseli.</summary>
    public string ImzaGorseliDataUri { get; set; } = string.Empty;
}

public sealed class OzelRaporListDto
{
    public string Id { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public string OlusturanKullaniciId { get; set; } = string.Empty;
    public string? OlusturanAdSoyad { get; set; }
    public IReadOnlyList<string> ErisebilenKullaniciIds { get; set; } = [];
    public IReadOnlyList<OzelRaporDosyaDto> Dosyalar { get; set; } = [];
    public DateTime OlusturmaTarihi { get; set; }
    public DateTime? GuncellenmeTarihi { get; set; }
    /// <summary>Çağıran kullanıcı bu raporu düzenleyebilir mi (oluşturan veya Sistem).</summary>
    public bool Duzenleyebilir { get; set; }
}

public sealed class OzelRaporUpsertRequest
{
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public List<string> ErisebilenKullaniciIds { get; set; } = [];
}
