namespace SayimLink.Api.Dtos.Rapor;

public sealed class MagazaSapmaDto
{
    public string MagazaId { get; set; } = string.Empty;
    public string MagazaAdi { get; set; } = string.Empty;
    public string FirmaAdi { get; set; } = string.Empty;
    public int OturumSayisi { get; set; }
    public int ToplamUrun { get; set; }
    public int ToplamFarkliUrun { get; set; }
    public decimal ToplamFarkPozitif { get; set; }
    public decimal ToplamFarkNegatif { get; set; }
    public decimal SapmaYuzdesi { get; set; }
}

public sealed class SaymanPerformansDto
{
    public string KullaniciId { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public int OturumSayisi { get; set; }
    public int ToplamGuncelleme { get; set; }
    public int ToplamYorum { get; set; }
    public DateTime? SonAktivite { get; set; }
}

public sealed class AuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime Tarih { get; set; }
    public string? KullaniciId { get; set; }
    public string KullaniciAdi { get; set; } = string.Empty;
    public string KullaniciRol { get; set; } = string.Empty;
    public string Aksiyon { get; set; } = string.Empty;
    public string? Hedef { get; set; }
    public string? HedefId { get; set; }
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }
    public string? IpAdres { get; set; }
    public bool Basarili { get; set; }
}

public sealed class AuditPageDto
{
    public IReadOnlyList<AuditLogDto> Items { get; set; } = [];
    public long Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
