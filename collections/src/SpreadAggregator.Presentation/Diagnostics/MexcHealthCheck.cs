using Microsoft.Extensions.Diagnostics.HealthChecks;
using SpreadAggregator.Application.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Presentation.Diagnostics;

/// <summary>
/// PHASE-2-FIX-6: Health check for Mexc WebSocket connection status
/// </summary>
public class MexcHealthCheck : IHealthCheck
{
    private readonly OrchestrationService _orchestrationService;

    public MexcHealthCheck(OrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var exchangeHealth = _orchestrationService.GetExchangeHealth();
        
        // Check if MEXC exchange is running
        if (exchangeHealth.TryGetValue("Mexc", out var status))
        {
            return status switch
            {
                "running" => Task.FromResult(HealthCheckResult.Healthy("MEXC WebSocket connection is active")),
                "failed" => Task.FromResult(HealthCheckResult.Unhealthy("MEXC WebSocket connection failed")),
                "stopped" => Task.FromResult(HealthCheckResult.Degraded("MEXC WebSocket connection stopped")),
                _ => Task.FromResult(HealthCheckResult.Unhealthy("MEXC WebSocket status unknown"))
            };
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("MEXC exchange not configured"));
    }
}
