using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Deviation Analysis Service - realtime arbitrage opportunity detection.
/// 
/// Coordinates price alignment and deviation calculation between exchange pairs.
/// Broadcasts deviation updates via WebSocket for frontend visualization.
/// 
/// Based on analyzer's join_asof and deviation formula.
/// </summary>
public class DeviationAnalysisService : IDisposable
{
    private readonly PriceAlignmentService _alignmentService;
    private readonly IWebSocketServer _webSocketServer;
    private readonly ILogger<DeviationAnalysisService> _logger;
    
    private readonly ConcurrentDictionary<string, decimal> _currentDeviations = new();
    private readonly System.Threading.PeriodicTimer _deviationTimer;
    private bool _disposed;
    
    private const int DEVIATION_UPDATE_INTERVAL_MS = 100; // 100ms updates (only 9 charts)
    
    public DeviationAnalysisService(
        PriceAlignmentService alignmentService,
        IWebSocketServer webSocketServer,
        ILogger<DeviationAnalysisService>? logger = null)
    {
        _alignmentService = alignmentService;
        _webSocketServer = webSocketServer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviationAnalysisService>.Instance;
        _deviationTimer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(DEVIATION_UPDATE_INTERVAL_MS));
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[DeviationAnalysis] Starting deviation analysis service...");
        _logger.LogInformation("[DeviationAnalysis] Starting...");
        
        await DeviationCalculationLoop(cancellationToken);
        
        Console.WriteLine("[DeviationAnalysis] Stopped");
        _logger.LogInformation("[DeviationAnalysis] Stopped");
    }
    
    /// <summary>
    /// Main loop: calculate deviations for all exchange pairs every 500ms.
    /// </summary>
    private async Task DeviationCalculationLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _deviationTimer.WaitForNextTickAsync(cancellationToken);
                
                await CalculateAndBroadcastDeviations();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DeviationAnalysis] Error in calculation loop");
            }
        }
    }
    
    /// <summary>
    /// Calculate deviations for all available symbols and exchange pairs.
    /// </summary>
    private async Task CalculateAndBroadcastDeviations()
    {
        var now = DateTime.UtcNow;
        var deviations = new List<DeviationData>();
        
        // Get all unique symbols from alignment service
        var symbols = GetAvailableSymbols();
        
        foreach (var symbol in symbols)
        {
            // Get all aligned pairs for this symbol
            var alignedPairs = _alignmentService.GetAllAlignedPairs(symbol, now);
            
            foreach (var (ex1, ex2, price1, price2) in alignedPairs)
            {
                // Calculate deviation
                var deviation = DeviationCalculator.CalculateDeviation(price1, price2);
                
                // Store current deviation
                var pairKey = $"{symbol}_{ex1}_{ex2}";
                _currentDeviations[pairKey] = deviation;
                
                // Add to broadcast list
                deviations.Add(new DeviationData
                {
                    Symbol = symbol,
                    Exchange1 = ex1,
                    Exchange2 = ex2,
                    Deviation = deviation,
                    Price1 = price1,
                    Price2 = price2,
                    Timestamp = now
                });
            }
        }
        
        // Broadcast all deviations
        if (deviations.Count > 0)
        {
            await BroadcastDeviations(deviations);
        }
    }
    
    /// <summary>
    /// Get all unique symbols that have data from multiple exchanges.
    /// </summary>
    private List<string> GetAvailableSymbols()
    {
        // Try common symbols first (assumes Binance and MEXC use same symbols)
        // In future: query from alignment service
        return new List<string>
        {
            "BTC_USDT",
            "ETH_USDT", 
            "SOL_USDT",
            "DOGE_USDT",
            "LINK_USDT",
            "SUI_USDT"
        };
    }
    
    /// <summary>
    /// Broadcast deviation updates to WebSocket clients.
    /// </summary>
    private async Task BroadcastDeviations(List<DeviationData> deviations)
    {
        var message = new
        {
            type = "deviation_update",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            count = deviations.Count,
            deviations = deviations.Select(d => new
            {
                symbol = d.Symbol,
                exchange1 = d.Exchange1,
                exchange2 = d.Exchange2,
                deviation_pct = d.Deviation,
                price1 = d.Price1,
                price2 = d.Price2,
                is_significant = DeviationCalculator.IsSignificantDeviation(d.Deviation, 0.2m),
                is_near_parity = DeviationCalculator.IsNearParity(d.Deviation, 0.05m)
            })
        };
        
        var json = JsonSerializer.Serialize(message);
        await _webSocketServer.BroadcastRealtimeAsync(json);
    }
    
    /// <summary>
    /// Get current deviation for a specific exchange pair.
    /// </summary>
    public decimal? GetCurrentDeviation(string symbol, string ex1, string ex2)
    {
        var pairKey = $"{symbol}_{ex1}_{ex2}";
        return _currentDeviations.TryGetValue(pairKey, out var deviation) ? deviation : null;
    }
    
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        _deviationTimer?.Dispose();
    }
}

/// <summary>
/// Deviation data structure for broadcasting.
/// </summary>
internal class DeviationData
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public required decimal Deviation { get; set; }
    public required decimal Price1 { get; set; }
    public required decimal Price2 { get; set; }
    public required DateTime Timestamp { get; set; }
}
