namespace SayimLink.Api.Dtos.Admin;

public sealed class KoordinatDto
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public sealed class MagazaDto
{
    public string Id { get; set; } = string.Empty;
    public string FirmaId { get; set; } = string.Empty;
    public string? FirmaAdi { get; set; }
    public string Ad { get; set; } = string.Empty;
    public string Sehir { get; set; } = string.Empty;
    public string Ilce { get; set; } = string.Empty;
    public string Adres { get; set; } = string.Empty;
    public KoordinatDto? Koordinat { get; set; }
    public string? MuduruKullaniciId { get; set; }
    public string? MuduruAdSoyad { get; set; }
    public bool AktifMi { get; set; }
}

public sealed class MagazaUpsertRequest
{
    public string FirmaId { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Sehir { get; set; } = string.Empty;
    public string Ilce { get; set; } = string.Empty;
    public string Adres { get; set; } = string.Empty;
    public KoordinatDto? Koordinat { get; set; }
    public string? MuduruKullaniciId { get; set; }
    public bool AktifMi { get; set; } = true;
}
