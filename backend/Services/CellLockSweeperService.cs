using Microsoft.AspNetCore.SignalR;
using SayimLink.Api.Hubs;

namespace SayimLink.Api.Services;

public sealed class CellLockSweeperService : BackgroundService
{
    private readonly ICellLockService _locks;
    private readonly IHubContext<SayimHub, ISayimHubClient> _hub;
    private readonly ILogger<CellLockSweeperService> _logger;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    public CellLockSweeperService(
        ICellLockService locks,
        IHubContext<SayimHub, ISayimHubClient> hub,
        ILogger<CellLockSweeperService> logger)
    {
        _locks = locks;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = _locks.SweepExpired();
                foreach (var l in expired)
                {
                    await _hub.Clients.Group($"oturum:{l.OturumId}")
                        .HucreSerbestBirakildi(l.OturumId, l.UrunId, l.Alan);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cell lock sweeper iteration failed");
            }
            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
