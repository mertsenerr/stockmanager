namespace SayimLink.Api.Configuration;

public sealed class OzelRaporSettings
{
    public const string SectionName = "OzelRapor";

    /// <summary>Lokal disk'te raporların saklanacağı kök dizin. Boşsa ContentRoot/App_Data/ozel-raporlar kullanılır.</summary>
    public string StorageRoot { get; set; } = string.Empty;

    /// <summary>Tek dosya için maksimum boyut (byte). Varsayılan 50 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } = [".xlsx", ".xls", ".pdf"];
}
