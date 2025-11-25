using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Diagnostics;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// MEXC Trades Aggregator with 30-minute incremental rolling window.
/// Collects trades from TradeScreenerChannel and broadcasts to WebSocket clients with pagination support.
/// </summary>
public class TradeAggregatorService : IDisposable
{
    private const int MAX_TRADES_PER_SYMBOL = 1000; // Memory bound per symbol
    private const int MAX_SYMBOLS = 5000; // LRU safety margin
    private readonly TimeSpan WINDOW_SIZE = TimeSpan.FromMinutes(30);
    private const int BATCH_INTERVAL_MS = 100; // 100ms batching for reduced CPU

    private readonly ChannelReader<MarketData> _channelReader;
    private readonly IWebSocketServer _webSocketServer;
    private readonly ILogger<TradeAggregatorService> _logger;
    private readonly PerformanceMonitor? _performanceMonitor;

    // Symbol → Queue<TradeData> (FIFO for incremental expiry)
    private readonly ConcurrentDictionary<string, Queue<TradeData>> _symbolTrades = new();

    // Symbol metadata: tickSize, lastPrice (for client pagination)
    private readonly ConcurrentDictionary<string, SymbolMetadata> _symbolMetadata = new();

    // Batching: accumulate trades per symbol before broadcast
    private readonly ConcurrentDictionary<string, List<TradeData>> _pendingBroadcasts = new();
    private readonly System.Threading.PeriodicTimer _batchTimer;
    private bool _disposed;

    public TradeAggregatorService(
        Channel<MarketData> tradeChannel,
        IWebSocketServer webSocketServer,
        ILogger<TradeAggregatorService>? logger = null,
        PerformanceMonitor? performanceMonitor = null)
    {
        _channelReader = tradeChannel.Reader;
        _webSocketServer = webSocketServer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TradeAggregatorService>.Instance;
        _performanceMonitor = performanceMonitor;
        _batchTimer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(BATCH_INTERVAL_MS));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TradeAggregator] Starting...");

        // Start batch broadcast timer
        _ = Task.Run(async () => await BatchBroadcastLoop(cancellationToken), cancellationToken);

        // Main trade processing loop
        await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
        {
            if (data is TradeData tradeData)
            {
                ProcessTrade(tradeData);
            }
        }

        _logger.LogInformation("[TradeAggregator] Channel closed, stopping.");
    }

    private void ProcessTrade(TradeData trade)
    {
        var key = $"{trade.Exchange}_{trade.Symbol}";
        var now = DateTime.UtcNow;

        // SPRINT-0-FIX-2: RecordEvent disabled (PerformanceMonitor is null)
        // Was causing string allocations on EVERY trade
        // _performanceMonitor?.RecordEvent($"Trade_{key}");

        // PHASE-1-FIX-4: Pre-check BEFORE adding to prevent symbol explosion
        if (_symbolTrades.Count >= MAX_SYMBOLS && !_symbolTrades.ContainsKey(key))
        {
            // Evict oldest symbol proactively
            var oldestKey = _symbolMetadata
                .OrderBy(x => x.Value.LastUpdate)
                .FirstOrDefault().Key;

            if (oldestKey != null)
            {
                _symbolTrades.TryRemove(oldestKey, out _);
                _symbolMetadata.TryRemove(oldestKey, out _);
                _pendingBroadcasts.TryRemove(oldestKey, out _);
                _logger.LogWarning($"[TradeAggregator] Evicted oldest symbol: {oldestKey} (LRU pre-check)");
            }
        }

        // 1. Get or create trade queue for this symbol
        var queue = _symbolTrades.GetOrAdd(key, _ => new Queue<TradeData>());

        lock (queue)
        {
            // 2. INCREMENTAL EXPIRY: Remove trades older than 30 min
            while (queue.Count > 0 && (now - queue.Peek().Timestamp) > WINDOW_SIZE)
            {
                queue.Dequeue();
            }

            // 3. Add new trade
            queue.Enqueue(trade);

            // 4. Enforce MAX_TRADES_PER_SYMBOL cap (LRU)
            if (queue.Count > MAX_TRADES_PER_SYMBOL)
            {
                queue.Dequeue(); // Remove oldest
            }
        }

        // 5. Update metadata (lastPrice for sorting)
        _symbolMetadata.AddOrUpdate(key,
            new SymbolMetadata { Symbol = trade.Symbol, LastPrice = trade.Price, LastUpdate = now },
            (_, existing) => { existing.LastPrice = trade.Price; existing.LastUpdate = now; return existing; });

        // 6. Add to pending broadcasts (batching)
        var pending = _pendingBroadcasts.GetOrAdd(key, _ => new List<TradeData>());
        lock (pending)
        {
            pending.Add(trade);
        }
    }

    /// <summary>
    /// Batching loop: flush pending broadcasts every 100ms + send metadata every 2 seconds
    /// </summary>
    private async Task BatchBroadcastLoop(CancellationToken cancellationToken)
    {
        int tickCounter = 0;
        const int METADATA_BROADCAST_INTERVAL = 20; // Every 20 ticks (2 seconds at 100ms interval)

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _batchTimer.WaitForNextTickAsync(cancellationToken);
                tickCounter++;

                // 1. ALWAYS send trade_update for pending trades (for charts)
                if (!_pendingBroadcasts.IsEmpty)
                {
                    var snapshot = _pendingBroadcasts.ToArray();
                    foreach (var kvp in snapshot)
                    {
                        _pendingBroadcasts.TryRemove(kvp.Key, out _);
                    }

                    // Broadcast trade updates
                    foreach (var (key, trades) in snapshot)
                    {
                        if (trades == null || trades.Count == 0) continue;

                        List<TradeData> tradesCopy;
                        lock (trades) { tradesCopy = trades.ToList(); }

                        var message = new
                        {
                            type = "trade_update",
                            symbol = key,
                            trades = tradesCopy.Select(t => new
                            {
                                price = t.Price,
                                quantity = t.Quantity,
                                side = t.Side,
                                timestamp = ((DateTimeOffset)t.Timestamp).ToUnixTimeMilliseconds()
                            })
                        };

                        var json = JsonSerializer.Serialize(message);
                        _ = _webSocketServer.BroadcastRealtimeAsync(json);
                    }
                }

                // 2. PERIODICALLY send all_symbols_scored (for sorting/metadata)
                if (tickCounter >= METADATA_BROADCAST_INTERVAL)
                {
                    tickCounter = 0;

                    var allMetadata = GetAllSymbolsMetadata().ToList();
                    if (allMetadata.Count > 0)
                    {
                        // Send all symbols with full metrics
                        var batchMessage = new
                        {
                            type = "all_symbols_scored",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            total = allMetadata.Count,
                            symbols = allMetadata.Select(m => new
                            {
                                symbol = m.Symbol,
                                score = m.Score,
                                tradesPerMin = m.TradesPerMin,
                                trades2m = m.Trades2Min,
                                trades3m = m.Trades3Min,
                                // SPRINT-2: Advanced benchmarks
                                acceleration = m.Acceleration,
                                hasPattern = m.HasVolumePattern,
                                imbalance = m.BuySellImbalance,
                                compositeScore = m.CompositeScore,
                                lastPrice = m.LastPrice,
                                lastUpdate = ((DateTimeOffset)m.LastUpdate).ToUnixTimeMilliseconds()
                            })
                        };

                        var json = JsonSerializer.Serialize(batchMessage);
                        _ = _webSocketServer.BroadcastRealtimeAsync(json);

                        // SPRINT-2: Send TOP-70 list separately (for chart rendering on client)
                        var top70 = allMetadata.Take(70).Select(m => m.Symbol).ToList();
                        var top70Message = new
                        {
                            type = "top70_update",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            symbols = top70
                        };

                        var top70Json = JsonSerializer.Serialize(top70Message);
                        _ = _webSocketServer.BroadcastRealtimeAsync(top70Json);

                        _logger.LogInformation("[TradeAggregator] Metadata broadcast: {Count} symbols, top composite: {TopScore:F1}",
                            allMetadata.Count,
                            allMetadata.First().CompositeScore);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TradeAggregator] Error in batch broadcast loop");
            }
        }
    }

    /// <summary>
    /// Calculate trades per minute for a symbol
    /// </summary>
    private int CalculateTradesPerMinute(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        int count = 0;

        lock (queue)
        {
            // Count trades in the last minute
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= oneMinuteAgo)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Calculate trades in the last 2 minutes for a symbol
    /// </summary>
    private int CalculateTrades2Min(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var twoMinutesAgo = DateTime.UtcNow.AddMinutes(-2);
        int count = 0;

        lock (queue)
        {
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= twoMinutesAgo)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Calculate trades in the last 3 minutes for a symbol
    /// </summary>
    private int CalculateTrades3Min(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var threeMinutesAgo = DateTime.UtcNow.AddMinutes(-3);
        int count = 0;

        lock (queue)
        {
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= threeMinutesAgo)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Calculate pump detection score for a symbol
    /// Formula: TradesPerMin × log10(VolumePerMin) - balances activity and volume
    /// </summary>
    private double CalculatePumpScore(string symbolKey, int tradesPerMin)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        decimal volumePerMin = 0;

        lock (queue)
        {
            // Calculate USD volume in the last minute
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= oneMinuteAgo)
                {
                    volumePerMin += trade.Price * trade.Quantity;
                }
            }
        }

        // Formula: trades × log10(volume)
        // Log prevents BTCUSDT from dominating, emphasizes relative activity
        if (volumePerMin <= 0) return tradesPerMin; // Fallback if no volume data

        return tradesPerMin * Math.Log10((double)volumePerMin + 1);
    }

    /// <summary>
    /// SPRINT-2: Calculate acceleration (growth rate of trading activity)
    /// Formula: trades1m / trades_in_previous_minute
    /// </summary>
    private double CalculateAcceleration(string symbolKey, int trades1m, int trades2m)
    {
        var tradesPreviousMin = trades2m - trades1m; // Trades between [-2m, -1m]
        if (tradesPreviousMin <= 0) return 1.0; // No acceleration data
        
        return (double)trades1m / tradesPreviousMin;
    }

    /// <summary>
    /// SPRINT-2: Detect volume patterns (repeated volumes = bot activity)
    /// Returns true if 10+ trades with same volume and side in last minute
    /// </summary>
    private bool DetectVolumePattern(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return false;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var recentTrades = new List<TradeData>();

        lock (queue)
        {
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= oneMinuteAgo)
                    recentTrades.Add(trade);
            }
        }

        // Group by volume and side, check for patterns
        var groups = recentTrades
            .GroupBy(t => new { Volume = t.Quantity, Side = t.Side })
            .Where(g => g.Count() >= 10);

        return groups.Any();
    }

    /// <summary>
    /// SPRINT-2: Calculate buy/sell imbalance
    /// Formula: |buyVolume - sellVolume| / (buyVolume + sellVolume)
    /// Returns 0-1 where 0 = balanced, 1 = completely one-sided
    /// </summary>
    private double CalculateBuySellImbalance(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        decimal buyVolume = 0;
        decimal sellVolume = 0;

        lock (queue)
        {
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= oneMinuteAgo)
                {
                    var volume = trade.Price * trade.Quantity;
                    if (trade.Side.Equals("Buy", StringComparison.OrdinalIgnoreCase))
                        buyVolume += volume;
                    else
                        sellVolume += volume;
                }
            }
        }

        var total = buyVolume + sellVolume;
        if (total == 0) return 0;

        return (double)Math.Abs(buyVolume - sellVolume) / (double)total;
    }

    /// <summary>
    /// SPRINT-2: Calculate composite score combining all benchmarks
    /// Formula: pumpScore * (1 + acceleration/2) + patternBonus + imbalanceBonus
    /// </summary>
    private double CalculateCompositeScore(
        double pumpScore, 
        double acceleration, 
        bool hasPattern, 
        double imbalance)
    {
        // Cap acceleration at 5.0 to prevent extreme outliers
        var cappedAcceleration = Math.Min(acceleration, 5.0);
        
        // Base score multiplied by acceleration factor
        var baseScore = pumpScore * (1.0 + cappedAcceleration / 2.0);
        
        // Pattern bonus (bot activity is interesting)
        var patternBonus = hasPattern ? 100.0 : 0.0;
        
        // Imbalance bonus (strong pressure is interesting)
        var imbalanceBonus = imbalance * 100.0;
        
        return baseScore + patternBonus + imbalanceBonus;
    }

    /// <summary>
    /// Get metadata for all symbols (for client pagination)
    /// SORTED BY ACTIVITY (trades per minute) - server-side smart sort!
    /// </summary>
    public IEnumerable<SymbolMetadata> GetAllSymbolsMetadata()
    {
        // STEP 1: Calculate basic metrics (trades/min, pump score) for ALL symbols
        var allWithBasicMetrics = _symbolMetadata.Values
            .Select(m =>
            {
                var symbolKey = $"{(m.Symbol.StartsWith("MEXC_") ? "" : "MEXC_")}{m.Symbol}";
                m.TradesPerMin = CalculateTradesPerMinute(symbolKey);
                m.Trades2Min = CalculateTrades2Min(symbolKey);
                m.Trades3Min = CalculateTrades3Min(symbolKey);
                m.Score = CalculatePumpScore(symbolKey, m.TradesPerMin);
                return m;
            })
            .OrderByDescending(m => m.Score)  // Sort by pump score
            .ToList();

        // STEP 2: OPTIMIZATION - Calculate advanced benchmarks only for TOP-500
        // This reduces CPU load from 2000 -> 500 symbols (4x improvement)
        var top500 = allWithBasicMetrics.Take(500).ToList();

        foreach (var m in top500)
        {
            var symbolKey = $"{(m.Symbol.StartsWith("MEXC_") ? "" : "MEXC_")}{m.Symbol}";
            
            // SPRINT-2: Calculate advanced benchmarks
            m.Acceleration = CalculateAcceleration(symbolKey, m.TradesPerMin, m.Trades2Min);
            m.HasVolumePattern = DetectVolumePattern(symbolKey);
            m.BuySellImbalance = CalculateBuySellImbalance(symbolKey);
            
            // Calculate composite score combining all benchmarks
            m.CompositeScore = CalculateCompositeScore(
                m.Score, 
                m.Acceleration, 
                m.HasVolumePattern, 
                m.BuySellImbalance
            );
        }

        // STEP 3: Sort TOP-500 by composite score, keep rest sorted by pump score
        var top500Sorted = top500.OrderByDescending(m => m.CompositeScore).ToList();
        var remaining = allWithBasicMetrics.Skip(500).ToList();

        // Return: TOP-500 by composite, then rest by pump score
        return top500Sorted.Concat(remaining).ToList();
    }

    /// <summary>
    /// SPRINT-2: Get TOP-70 symbols by composite score (for chart rendering)
    /// </summary>
    public List<string> GetTop70Symbols()
    {
        var allMetadata = GetAllSymbolsMetadata();
        return allMetadata
            .Take(70)
            .Select(m => m.Symbol)
            .ToList();
    }

    /// <summary>
    /// Get trades for specific symbols (for initial load when client subscribes to page)
    /// </summary>
    public Dictionary<string, List<TradeData>> GetTradesForSymbols(IEnumerable<string> symbolKeys)
    {
        var result = new Dictionary<string, List<TradeData>>();

        foreach (var key in symbolKeys)
        {
            if (_symbolTrades.TryGetValue(key, out var queue))
            {
                lock (queue)
                {
                    result[key] = queue.ToList();
                }
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _batchTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Metadata for symbol pagination and sorting
/// </summary>
public class SymbolMetadata
{
    public required string Symbol { get; set; }
    public decimal LastPrice { get; set; }
    public DateTime LastUpdate { get; set; }
    public int TradesPerMin { get; set; }  // Activity metric for sorting (1 minute)
    public int Trades2Min { get; set; }    // Trades in last 2 minutes (for acceleration detection)
    public int Trades3Min { get; set; }    // Trades in last 3 minutes (for trend analysis)
    public double Score { get; set; }  // Pump detection score (for real-time sorting)
    
    // SPRINT-2: Advanced benchmarks
    public double Acceleration { get; set; }        // Growth rate (trades1m / trades2m_prev)
    public bool HasVolumePattern { get; set; }      // Repeated volumes (bot detection)
    public double BuySellImbalance { get; set; }    // Buy/sell pressure (0-1)
    public double CompositeScore { get; set; }      // Weighted sum of all benchmarks
}
