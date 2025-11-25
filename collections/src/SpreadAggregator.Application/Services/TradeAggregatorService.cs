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
                                lastPrice = m.LastPrice,
                                lastUpdate = ((DateTimeOffset)m.LastUpdate).ToUnixTimeMilliseconds()
                            })
                        };

                        var json = JsonSerializer.Serialize(batchMessage);
                        _ = _webSocketServer.BroadcastRealtimeAsync(json);

                        _logger.LogInformation("[TradeAggregator] Metadata broadcast: {Count} symbols, top score: {TopScore:F1}",
                            allMetadata.Count,
                            allMetadata.First().Score);
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
    /// Get metadata for all symbols (for client pagination)
    /// SORTED BY ACTIVITY (trades per minute) - server-side smart sort!
    /// </summary>
    public IEnumerable<SymbolMetadata> GetAllSymbolsMetadata()
    {
        // Calculate trades/min and pump score for each symbol
        return _symbolMetadata.Values
            .Select(m =>
            {
                var symbolKey = $"{(m.Symbol.StartsWith("MEXC_") ? "" : "MEXC_")}{m.Symbol}";
                m.TradesPerMin = CalculateTradesPerMinute(symbolKey);
                m.Score = CalculatePumpScore(symbolKey, m.TradesPerMin);
                return m;
            })
            .OrderByDescending(m => m.Score)  // Sort by pump score (hottest first)
            .ThenByDescending(m => m.LastUpdate)  // Tie-breaker
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
    public int TradesPerMin { get; set; }  // Activity metric for sorting
    public double Score { get; set; }  // Pump detection score (for real-time sorting)
}
