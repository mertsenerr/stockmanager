using System.Security.Claims;

namespace SayimLink.Api.Models;

public static class Roles
{
    // Yeni rol modeli (Faz: register + multi-tenant).
    public const string Sistem = "Sistem";          // süper admin (platformu yöneten)
    public const string SayimBaskani = "SayimBaskani"; // bir firmanın yöneticisi
    public const string Kullanici = "Kullanici";    // mağaza/şube tarafı

    // Geri uyumluluk için eski isimler yeni rollere aliaslıdır.
    // [Authorize(Roles = Roles.Admin)] gibi kullanımlar string değerinin
    // değişmesinden etkilenir; mevcut DB değerleri AdminSeederHostedService
    // tarafından yeni isimlere migrate edilir.
    public const string Admin = Sistem;
    public const string SayimYoneticisi = SayimBaskani;
    public const string MagazaMuduru = Kullanici;
    public const string Sayman = Kullanici;

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Sistem, SayimBaskani, Kullanici,
    };

    public static bool IsValid(string role) => All.Contains(role);

    /// <summary>Admin-seviyesi rol mü? (Sistem veya SayimBaskani)</summary>
    public static bool IsAdminLevel(ClaimsPrincipal user) =>
        user.IsInRole(Sistem) || user.IsInRole(SayimBaskani);

    /// <summary>Admin attribute string'i — `[Authorize(Roles = Roles.AdminLevel)]` için.</summary>
    public const string AdminLevel = Sistem + "," + SayimBaskani;
}
