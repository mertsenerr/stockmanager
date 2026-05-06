using System.Security.Claims;
using SayimLink.Api.Models;

namespace SayimLink.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? GetFirmaId(this ClaimsPrincipal user) =>
        user.FindFirst("firmaId")?.Value is { Length: > 0 } id ? id : null;

    public static bool IsSistem(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Sistem);
}
