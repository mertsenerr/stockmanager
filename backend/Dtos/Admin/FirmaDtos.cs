namespace SayimLink.Api.Dtos.Admin;

public sealed class FirmaDto
{
    public string Id { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string Kisaltma { get; set; } = string.Empty;
    public string Tip { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool AktifMi { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
}

public sealed class FirmaUpsertRequest
{
    public string Ad { get; set; } = string.Empty;
    public string? Kisaltma { get; set; }
    public string Tip { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public bool AktifMi { get; set; } = true;
}
