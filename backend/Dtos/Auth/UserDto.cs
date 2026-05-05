namespace SayimLink.Api.Dtos.Auth;

public sealed class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string? FirmaId { get; set; }
    public string? FirmaAdi { get; set; }
    public string? FirmaKisaltmasi { get; set; }
    public IReadOnlyList<string> FirmaIds { get; set; } = [];
    public IReadOnlyList<string> MagazaIds { get; set; } = [];
    public bool Onayli { get; set; } = true;
}
