namespace SayimLink.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "SayimLinkClient";

    public string[] AllowedOrigins { get; set; } = [];
}
