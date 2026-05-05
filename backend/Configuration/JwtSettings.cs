namespace SayimLink.Api.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "sayimlink-api";
    public string Audience { get; set; } = "sayimlink-app";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDaysRememberMe { get; set; } = 30;
    public int RefreshTokenHoursDefault { get; set; } = 8;
}
