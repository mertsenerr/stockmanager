namespace SayimLink.Api.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "sayimlink-api";
    public string Audience { get; set; } = "sayimlink-app";
    // Short by design: the access token is stateless, so a compromised one is
    // valid for this whole window. The OnTokenValidated hook gives us on-demand
    // revocation (TokenInvalidatedAt), but keeping the natural expiry short is
    // the cheap defence-in-depth that doesn't need DB lookups.
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDaysRememberMe { get; set; } = 30;
    public int RefreshTokenHoursDefault { get; set; } = 8;
}
