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
    public string? AvatarDataUri { get; set; }
}

public sealed class UpdateAvatarRequest
{
    /// <summary>data:image/(png|jpeg|webp);base64,... formatında. null → avatarı kaldırır (DELETE endpoint).</summary>
    public string? DataUri { get; set; }
}
