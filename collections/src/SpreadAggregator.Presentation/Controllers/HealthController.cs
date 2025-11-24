using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using System;

namespace SpreadAggregator.Presentation.Controllers;

/// <summary>
/// PROPOSAL-2025-0095 + PHASE-2-FIX-6: Health check endpoint for monitoring
/// Updated: Removed RollingWindowService dependency (legacy HFT code)
/// </summary>
[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    private readonly OrchestrationService _orchestration;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public HealthController(OrchestrationService orchestration)
    {
        _orchestration = orchestration;
    }

    /// <summary>
    /// Health check endpoint - returns system status and metrics
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        var uptime = DateTime.UtcNow - _startTime;

        var health = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            uptime = new
            {
                days = uptime.Days,
                hours = uptime.Hours,
                minutes = uptime.Minutes,
                totalSeconds = (int)uptime.TotalSeconds
            },
            memory = new
            {
                workingSetMB = GC.GetTotalMemory(false) / 1024 / 1024,
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2)
            },
            services = new
            {
                exchanges = _orchestration.GetExchangeHealth()
            }
        };

        return Ok(health);
    }

    /// <summary>
    /// Simple alive check - minimal response
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
    }
}
