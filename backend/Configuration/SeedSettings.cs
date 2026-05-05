namespace SayimLink.Api.Configuration;

public sealed class SeedSettings
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string AdminName { get; set; } = "Sayım Başkanı";
}
