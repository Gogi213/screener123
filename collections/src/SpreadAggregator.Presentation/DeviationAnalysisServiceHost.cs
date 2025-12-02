using Microsoft.Extensions.Hosting;
using SpreadAggregator.Application.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Presentation;

/// <summary>
/// Hosted service wrapper for DeviationAnalysisService.
/// Starts deviation calculation loop on application startup.
/// </summary>
public class DeviationAnalysisServiceHost : IHostedService
{
    private readonly DeviationAnalysisService _deviationService;
    private Task? _runningTask;
    private CancellationTokenSource? _cts;

    public DeviationAnalysisServiceHost(DeviationAnalysisService deviationService)
    {
        _deviationService = deviationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[DeviationHost] Starting deviation analysis service host...");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runningTask = _deviationService.StartAsync(_cts.Token);
        Console.WriteLine("[DeviationHost] Deviation service task started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runningTask == null)
            return;

        _cts?.Cancel();
        await _runningTask;
        _deviationService.Dispose();
    }
}
