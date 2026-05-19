using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using SayimLink.Api.Configuration;

namespace SayimLink.Api.Services;

public interface IOzelRaporStorage
{
    /// <summary>Yüklenecek dosyanın boyut/uzantı validasyonu. Geçerliyse null, değilse hata mesajı.</summary>
    string? ValidateUpload(string fileName, long size);

    /// <summary>Dosyayı saklamaya yazar, kaydedilen storage adını (GridFS ObjectId string) döner.</summary>
    Task<string> SaveAsync(string raporId, string storageId, string originalFileName, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string raporId, string storageName, CancellationToken ct);

    void DeleteFile(string raporId, string storageName);

    void DeleteRaporFolder(string raporId);

    long MaxFileSizeBytes { get; }
}

/// <summary>
/// MongoDB GridFS tabanlı dosya saklama. Render gibi PaaS'larda kalıcı disk
/// gerektirmez — dosyalar Mongo'da chunk'lar halinde saklanır. StorageName,
/// GridFS dosyasının ObjectId'sinin string halidir.
/// </summary>
public sealed class OzelRaporStorage : IOzelRaporStorage
{
    private const string BucketName = "ozel_rapor";
    private readonly OzelRaporSettings _settings;
    private readonly IGridFSBucket _bucket;

    public OzelRaporStorage(IOptions<OzelRaporSettings> options, IMongoDbService mongo)
    {
        _settings = options.Value;
        _bucket = new GridFSBucket(mongo.Database, new GridFSBucketOptions { BucketName = BucketName });
    }

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
        // GridFS metadata — DeleteRaporFolder filtre çekerken kullanıyor, mimeType da
        // download üzerinde Content-Type belirleme şansı verir.
        var ext = Path.GetExtension(originalFileName);
        var meta = new BsonDocument
        {
            { "raporId", raporId },
            { "dosyaId", storageId },
            { "ext", ext.ToLowerInvariant() },
        };
        var opts = new GridFSUploadOptions { Metadata = meta };
        // Filename'i {storageId}{ext} olarak yazıyoruz — orijinal ad zaten OzelRaporDosya.Ad'da.
        var id = await _bucket.UploadFromStreamAsync($"{storageId}{ext}", content, opts, ct);
        return id.ToString();
    }

    public async Task<Stream> OpenReadAsync(string raporId, string storageName, CancellationToken ct)
    {
        if (!ObjectId.TryParse(storageName, out var oid))
            throw new FileNotFoundException("Rapor dosyası bulunamadı (geçersiz id).", storageName);
        try
        {
            return await _bucket.OpenDownloadStreamAsync(oid, cancellationToken: ct);
        }
        catch (GridFSFileNotFoundException)
        {
            throw new FileNotFoundException("Rapor dosyası bulunamadı.", storageName);
        }
    }

    public void DeleteFile(string raporId, string storageName)
    {
        if (!ObjectId.TryParse(storageName, out var oid)) return;
        try { _bucket.Delete(oid); }
        catch (GridFSFileNotFoundException) { /* zaten yok, sorun değil */ }
    }

    public void DeleteRaporFolder(string raporId)
    {
        // GridFS'de "klasör" kavramı yok — metadata.raporId üzerinden filtreliyoruz.
        var filter = Builders<GridFSFileInfo>.Filter.Eq("metadata.raporId", raporId);
        using var cursor = _bucket.Find(filter);
        foreach (var file in cursor.ToEnumerable())
        {
            try { _bucket.Delete(file.Id); }
            catch (GridFSFileNotFoundException) { /* paralel silinmiş olabilir */ }
        }
    }
}
