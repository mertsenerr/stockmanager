namespace SayimLink.Api.Dtos.Sayim;

public sealed class OturumOzetDto
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

public sealed class KatilimciDto
{
    public string KullaniciId { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public sealed class DavetEdilebilirKullaniciDto
{
    public string Id { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool ZatenKatilimci { get; set; }
}

public class OturumListDto
{
    public string Id { get; set; } = string.Empty;
    public string MagazaId { get; set; } = string.Empty;
    public string MagazaAdi { get; set; } = string.Empty;
    public string FirmaId { get; set; } = string.Empty;
    public string FirmaAdi { get; set; } = string.Empty;
    public string FirmaTip { get; set; } = string.Empty;
    public string? AtamaId { get; set; }
    public DateTime Tarih { get; set; }
    public string Durum { get; set; } = string.Empty;
    public OturumOzetDto Ozetler { get; set; } = new();
    public IReadOnlyList<KatilimciDto> Katilimcilar { get; set; } = [];
    public IReadOnlyList<string> DavetliMailler { get; set; } = [];
    public DateTime OlusturmaTarihi { get; set; }
}

public sealed class UrunYorumDto
{
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;
    public string Mesaj { get; set; } = string.Empty;
    public DateTime Tarih { get; set; }
}

public sealed class UrunDegisiklikDto
{
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;
    public string Alan { get; set; } = string.Empty;
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }
    public DateTime Tarih { get; set; }
}

public sealed class UrunDegisiklikTalebiDto
{
    public string Id { get; set; } = string.Empty;
    public string KullaniciId { get; set; } = string.Empty;
    public string KullaniciAdi { get; set; } = string.Empty;
    public string Alan { get; set; } = string.Empty;
    public string? EskiDeger { get; set; }
    public string? YeniDeger { get; set; }
    public string? Gerekce { get; set; }
    public string Durum { get; set; } = string.Empty;
    public string? KararVerenId { get; set; }
    public string? KararVerenAdi { get; set; }
    public string? KararSebep { get; set; }
    public DateTime? KararTarihi { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
    public string UrunId { get; set; } = string.Empty;
}

public sealed class OturumUrunDto
{
    public string Id { get; set; } = string.Empty;
    public string Barkod { get; set; } = string.Empty;
    public string UrunAdi { get; set; } = string.Empty;
    public decimal SistemStok { get; set; }
    public decimal SayilanStok { get; set; }
    public decimal Fark { get; set; }
    public string Durum { get; set; } = string.Empty;
    public string? AtananSaymanId { get; set; }
    public string? AtananSaymanAdi { get; set; }
    public int YorumSayisi { get; set; }
    public string? KilitleyenKullaniciId { get; set; }
    public string? KilitleyenAdi { get; set; }
    public DateTime? KilitlenmeTarihi { get; set; }

    public string? StokKodu { get; set; }
    public string? Kategori { get; set; }
    public string? AltKategori { get; set; }
    public string? Renk { get; set; }
    public string? Beden { get; set; }
    public string? Marka { get; set; }
    public string? Model { get; set; }
    public decimal? Fiyat { get; set; }

    public decimal? SistemFarki { get; set; }
    public decimal? FiiliFarki { get; set; }
    public decimal? FiyatFarki { get; set; }

    /// <summary>Bu ürün için açık (beklemede) değişiklik talepleri.</summary>
    public IReadOnlyList<UrunDegisiklikTalebiDto> AcikTalepler { get; set; } = [];
}

public sealed class OturumDetailDto : OturumListDto
{
    public IReadOnlyList<OturumUrunDto> Urunler { get; set; } = [];
    public ExcelMappingDto ExcelMapping { get; set; } = new();
}

public sealed class ExcelMappingDto
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

public sealed class OturumCreateRequest
{
    public string MagazaId { get; set; } = string.Empty;
    /// <summary>yyyy-MM-dd</summary>
    public string Tarih { get; set; } = string.Empty;
    public string? AtamaId { get; set; }
    public List<KatilimciCreateDto> Katilimcilar { get; set; } = [];
    public List<string> DavetliMailler { get; set; } = [];
}

public sealed class KatilimciCreateDto
{
    public string KullaniciId { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
}

public sealed class OturumUpdateRequest
{
    /// <summary>yyyy-MM-dd</summary>
    public string Tarih { get; set; } = string.Empty;
    public List<KatilimciCreateDto> Katilimcilar { get; set; } = [];
    public List<string> DavetliMailler { get; set; } = [];
    public string? AtamaId { get; set; }
}

public sealed class OturumDurumChangeRequest
{
    public string Durum { get; set; } = string.Empty;
}

public sealed class ExcelImportRow
{
    public string Barkod { get; set; } = string.Empty;
    public string UrunAdi { get; set; } = string.Empty;
    public decimal SistemStok { get; set; }
    public decimal SayilanStok { get; set; }
    public string? StokKodu { get; set; }
    public string? Kategori { get; set; }
    public string? AltKategori { get; set; }
    public string? Renk { get; set; }
    public string? Beden { get; set; }
    public string? Marka { get; set; }
    public string? Model { get; set; }
    public decimal? Fiyat { get; set; }
}

public sealed class ExcelImportRequest
{
    public ExcelMappingDto Mapping { get; set; } = new();
    public List<ExcelImportRow> Urunler { get; set; } = [];
}

public sealed class UrunPatchRequest
{
    public decimal? SayilanStok { get; set; }
    public string? Durum { get; set; }
    public string? AtananSaymanId { get; set; }
    public string? YorumEkle { get; set; }
    public string? Barkod { get; set; }
    public string? UrunAdi { get; set; }
    public decimal? SistemStok { get; set; }
}
