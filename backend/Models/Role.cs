using System.Security.Claims;

namespace SayimLink.Api.Models;

public static class Roles
{
    // Yeni rol modeli (Faz: register + multi-tenant).
    public const string Sistem = "Sistem";          // süper admin (platformu yöneten)
    public const string SayimBaskani = "SayimBaskani"; // bir firmanın yöneticisi
    public const string Kullanici = "Kullanici";    // mağaza/şube tarafı

    // ⚠️ READ THIS BEFORE TOUCHING ROLE COMPARISONS ⚠️
    // The "tenant-side" identities — MagazaMuduru, Sayman — are aliases for the
    // single underlying role string "Kullanici". A user in the system has exactly
    // one role value; "Sayman" vs "MagazaMuduru" is a UI/intent label, not a
    // distinct authorization tier. Consequence: `User.IsInRole(Roles.Sayman)`
    // returns true for every Kullanici, including those acting as MagazaMuduru.
    //
    // If you need to distinguish at runtime (e.g. "is this user actually counting,
    // or just supervising a store?"), gate on a separate signal such as
    // dbUser.MagazaIds.Count > 0 or atama.SaymanKullaniciIds.Contains(uid).
    // DO NOT add new behavioural branches that key off these aliases alone — they
    // will fire for the wrong users.
    //
    // Admin / SayimYoneticisi aliases for Sistem / SayimBaskani are similarly
    // backwards-compat shims; the new names are canonical.
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
