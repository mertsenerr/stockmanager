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
