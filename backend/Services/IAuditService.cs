using System.Threading.Channels;
using SayimLink.Api.Models;
using SayimLink.Api.Repositories;

namespace SayimLink.Api.Services;

public interface IAuditService
{
    void Enqueue(AuditLog log);
    AuditLog Build(
        string aksiyon,
        string? kullaniciId,
        string? kullaniciAdi,
        string? rol,
        string? hedef = null,
        string? hedefId = null,
        string? eskiDeger = null,
        string? yeniDeger = null,
        string? ip = null,
        string? userAgent = null,
        bool basarili = true);
}

public sealed class AuditService : IAuditService
{
    // Per-field char cap. Many callers splice user-controlled strings into
    // eskiDeger / yeniDeger (e.g. a urun yorum, a sayım change list); without a
    // cap an attacker could keep posting maximum-length comments to bloat the
    // audit collection until it fills the Atlas free tier disk quota.
    private const int FieldCharLimit = 4_000;

    private readonly Channel<AuditLog> _channel;

    public AuditService()
    {
        _channel = Channel.CreateBounded<AuditLog>(new BoundedChannelOptions(8192)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    internal ChannelReader<AuditLog> Reader => _channel.Reader;

    public void Enqueue(AuditLog log)
    {
        _channel.Writer.TryWrite(log);
    }

    public AuditLog Build(
        string aksiyon,
        string? kullaniciId,
        string? kullaniciAdi,
        string? rol,
        string? hedef = null,
        string? hedefId = null,
        string? eskiDeger = null,
        string? yeniDeger = null,
        string? ip = null,
        string? userAgent = null,
        bool basarili = true) => new()
    {
        Aksiyon = aksiyon,
        KullaniciId = kullaniciId,
        KullaniciAdi = Truncate(kullaniciAdi ?? "?", 160) ?? "?",
        KullaniciRol = rol ?? string.Empty,
        Hedef = hedef,
        HedefId = hedefId,
        EskiDeger = Truncate(eskiDeger, FieldCharLimit),
        YeniDeger = Truncate(yeniDeger, FieldCharLimit),
        IpAdres = ip,
        UserAgent = Truncate(userAgent, 500),
        Basarili = basarili,
    };

    private static string? Truncate(string? s, int max)
    {
        if (s is null) return null;
        return s.Length <= max ? s : s[..max] + "…[truncated]";
    }
}

public sealed class AuditWriterService : BackgroundService
{
    private readonly AuditService _service;
    private readonly IServiceProvider _sp;
    private readonly ILogger<AuditWriterService> _logger;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    public AuditWriterService(IAuditService service, IServiceProvider sp, ILogger<AuditWriterService> logger)
    {
        _service = (AuditService)service;
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLog>(64);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (batch.Count < 256 && _service.Reader.TryRead(out var log))
                    batch.Add(log);

                if (batch.Count > 0)
                {
                    using var scope = _sp.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
                    await repo.InsertManyAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit write batch failed");
                batch.Clear();
            }
            try { await Task.Delay(FlushInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
