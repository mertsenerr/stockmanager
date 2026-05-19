namespace SayimLink.Api.Configuration;

public sealed class OzelRaporSettings
{
    public const string SectionName = "OzelRapor";

    /// <summary>Tek dosya için maksimum boyut (byte). Varsayılan 50 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } = [".xlsx", ".xls", ".pdf", ".csv"];
}
