using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services.Background;

/// <summary>
/// Owns the orbital data lifecycle:
///   - On startup: one immediate fetch + propagation cycle.
///   - Every TleFetchIntervalMinutes (default 60): refresh TLEs from CelesTrak (+ KeepTrack if available).
///   - Every PropagationIntervalSeconds (default 60): re-propagate all cached objects to current time.
///
/// Never crashes the host. Each loop iteration is exception-isolated.
/// OperationCanceledException (shutdown) is always re-thrown from loops.
/// </summary>
public sealed class TleIngestionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOrbitalCache _cache;
    private readonly OrbitalSettings _settings;
    private readonly ILogger<TleIngestionService> _logger;

    public TleIngestionService(
        IServiceProvider services,
        IOrbitalCache cache,
        IOptions<OrbitalSettings> settings,
        ILogger<TleIngestionService> logger)
    {
        _services = services;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TleIngestionService starting — executing initial cycle");

        var fetchOk = await SafeFetchAsync(stoppingToken);
        if (!fetchOk)
            _logger.LogCritical(
                "Initial CelesTrak fetch FAILED — cache is empty. Will retry in {Minutes} minutes.",
                _settings.TleFetchIntervalMinutes);

        await SafePropagateAsync(stoppingToken);

        await Task.WhenAll(
            RunFetchLoopAsync(stoppingToken),
            RunPropagateLoopAsync(stoppingToken));
    }

    // ── fetch loop ────────────────────────────────────────────────────────────

    private async Task RunFetchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.TleFetchIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafeFetchAsync(ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task<bool> SafeFetchAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("TleIngestion: starting fetch cycle");
            using var scope = _services.CreateScope();
            var aggregator = scope.ServiceProvider.GetRequiredService<IDataAggregatorService>();
            await aggregator.FetchAndMergeAsync(ct);
            _logger.LogInformation("TleIngestion: fetch complete — {Count} objects in cache", _cache.Count);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: fetch cycle failed — will retry at next interval");
            return false;
        }
    }

    // ── propagation loop ──────────────────────────────────────────────────────

    private async Task RunPropagateLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.PropagationIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafePropagateAsync(ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task SafePropagateAsync(CancellationToken ct)
    {
        try
        {
            var objects = _cache.GetAll();
            if (objects.Count == 0)
            {
                _logger.LogDebug("TleIngestion: no objects to propagate yet");
                return;
            }

            using var scope = _services.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IOrbitalEngineService>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var propagated = await Task.Run(
                () => engine.PropagateAll(objects, DateTime.UtcNow), ct);
            sw.Stop();

            _cache.Update(propagated);

            _logger.LogInformation(
                "TleIngestion: propagated {Count}/{Total} objects in {Ms} ms",
                propagated.Count, objects.Count, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: propagation cycle failed — will retry at next interval");
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("TleIngestionService stopping");
        await base.StopAsync(ct);
    }
}
