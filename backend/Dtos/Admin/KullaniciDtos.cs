namespace SayimLink.Api.Dtos.Admin;

public sealed class KullaniciListDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string? FirmaId { get; set; }
    public IReadOnlyList<string> FirmaIds { get; set; } = [];
    public IReadOnlyList<string> MagazaIds { get; set; } = [];
    public bool AktifMi { get; set; }
    public bool Onayli { get; set; } = true;
    public DateTime? SonGirisTarihi { get; set; }
    public DateTime OlusturmaTarihi { get; set; }
}

public sealed class KullaniciCreateRequest
{
    public string Email { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> FirmaIds { get; set; } = [];
    public List<string> MagazaIds { get; set; } = [];
    public bool AktifMi { get; set; } = true;
}

public sealed class KullaniciUpdateRequest
{
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public List<string> FirmaIds { get; set; } = [];
    public List<string> MagazaIds { get; set; } = [];
    public bool AktifMi { get; set; } = true;
    public string? NewPassword { get; set; }
}
