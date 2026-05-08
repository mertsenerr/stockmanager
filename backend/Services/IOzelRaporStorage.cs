using Microsoft.Extensions.Options;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface IOzelRaporStorage
{
    /// <summary>Yüklenecek dosyanın boyut/uzantı validasyonu. Geçerliyse null, değilse hata mesajı.</summary>
    string? ValidateUpload(string fileName, long size);

    /// <summary>Dosyayı diske yazar, kaydedilen storage adını (id+ext) döner.</summary>
    Task<string> SaveAsync(string raporId, string storageId, string originalFileName, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string raporId, string storageName, CancellationToken ct);

    void DeleteFile(string raporId, string storageName);

    void DeleteRaporFolder(string raporId);

    long MaxFileSizeBytes { get; }
}

public sealed class OzelRaporStorage : IOzelRaporStorage
{
    private readonly OzelRaporSettings _settings;
    private readonly string _root;

    public OzelRaporStorage(IOptions<OzelRaporSettings> options, IHostEnvironment env)
    {
        _settings = options.Value;
        _root = string.IsNullOrWhiteSpace(_settings.StorageRoot)
            ? Path.Combine(env.ContentRootPath, "App_Data", "ozel-raporlar")
            : _settings.StorageRoot;
    }

    private void EnsureRoot() => Directory.CreateDirectory(_root);

    public long MaxFileSizeBytes => _settings.MaxFileSizeBytes;

    public string? ValidateUpload(string fileName, long size)
    {
        if (size <= 0) return "Dosya boş.";
        if (size > _settings.MaxFileSizeBytes)
            return $"Dosya boyutu {_settings.MaxFileSizeBytes / (1024 * 1024)} MB sınırını aşıyor.";

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !_settings.AllowedExtensions.Contains(ext))
            return $"Sadece şu uzantılar yüklenebilir: {string.Join(", ", _settings.AllowedExtensions)}";

        return null;
    }

    public async Task<string> SaveAsync(
        string raporId, string storageId, string originalFileName, Stream content, CancellationToken ct)
    {
        EnsureRoot();
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var storageName = storageId + ext;
        var folder = Path.Combine(_root, raporId);
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, storageName);
        await using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);
        return storageName;
    }

    public Task<Stream> OpenReadAsync(string raporId, string storageName, CancellationToken ct)
    {
        var path = Path.Combine(_root, raporId, storageName);
        if (!File.Exists(path)) throw new FileNotFoundException("Rapor dosyası bulunamadı.", path);
        Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(s);
    }

    public void DeleteFile(string raporId, string storageName)
    {
        var path = Path.Combine(_root, raporId, storageName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteRaporFolder(string raporId)
    {
        var folder = Path.Combine(_root, raporId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
