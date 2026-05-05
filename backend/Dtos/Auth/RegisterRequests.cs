namespace SayimLink.Api.Dtos.Auth;

public sealed class RegisterSayimBaskaniRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public string FirmaAdi { get; set; } = string.Empty;
    public string FirmaKisaltmasi { get; set; } = string.Empty;
}

public sealed class RegisterKullaniciRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    /// <summary>Firma katılım anahtarı (kısaltma).</summary>
    public string FirmaKisaltmasi { get; set; } = string.Empty;
}
