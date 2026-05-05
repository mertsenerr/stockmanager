namespace SayimLink.Api.Dtos.Takvim;

public sealed class AtamaDto
{
    public string Id { get; set; } = string.Empty;
    public string MagazaId { get; set; } = string.Empty;
    public string MagazaAdi { get; set; } = string.Empty;
    public string FirmaId { get; set; } = string.Empty;
    public string FirmaAdi { get; set; } = string.Empty;
    public DateTime Tarih { get; set; }
    public string? BaslangicSaati { get; set; }
    public string? BitisSaati { get; set; }
    public string YoneticiKullaniciId { get; set; } = string.Empty;
    public string YoneticiAdi { get; set; } = string.Empty;
    public IReadOnlyList<string> SaymanKullaniciIds { get; set; } = [];
    public IReadOnlyList<string> SaymanAdlari { get; set; } = [];
    public string? Notlar { get; set; }
    public string Durum { get; set; } = string.Empty;
}

public sealed class AtamaUpsertRequest
{
    public string MagazaId { get; set; } = string.Empty;
    /// <summary>ISO date string (yyyy-MM-dd) — server normalizes to UTC midnight.</summary>
    public string Tarih { get; set; } = string.Empty;
    public string? BaslangicSaati { get; set; }
    public string? BitisSaati { get; set; }
    public string YoneticiKullaniciId { get; set; } = string.Empty;
    public List<string> SaymanKullaniciIds { get; set; } = [];
    public string? Notlar { get; set; }
    public string Durum { get; set; } = "planlandi";
}

public sealed class AtamaTarihUpdateRequest
{
    public string Tarih { get; set; } = string.Empty;
}
