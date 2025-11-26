using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
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
    private const int MAX_TRADES_PER_SYMBOL = 5000; // FIXED: Increased from 1000 to allow accurate statistics for active pairs
    private const int MAX_SYMBOLS = 5000; // LRU safety margin
    private readonly TimeSpan WINDOW_SIZE = TimeSpan.FromMinutes(30);
    private const int BATCH_INTERVAL_MS = 200; // SPRINT-8: 200ms batching for reduced CPU (~50% fewer broadcasts)

    private readonly ChannelReader<MarketData> _channelReader;
    private readonly IWebSocketServer _webSocketServer;
    private readonly ILogger<TradeAggregatorService> _logger;

    // Symbol → Queue<TradeData> (FIFO for incremental expiry)
    private readonly ConcurrentDictionary<string, Queue<TradeData>> _symbolTrades = new();

    // Symbol metadata: tickSize, lastPrice (for client pagination)
    private readonly ConcurrentDictionary<string, SymbolMetadata> _symbolMetadata = new();

    // SPRINT-10: Ticker data storage (Volume24h, PriceChangePercent24h)
    private readonly ConcurrentDictionary<string, TickerData> _tickerData = new();

    // Batching: accumulate trades per symbol before broadcast
    private readonly ConcurrentDictionary<string, List<TradeData>> _pendingBroadcasts = new();
    private readonly System.Threading.PeriodicTimer _batchTimer;
    private bool _disposed;

    public TradeAggregatorService(
        Channel<MarketData> tradeChannel,
        IWebSocketServer webSocketServer,
        ILogger<TradeAggregatorService>? logger = null)
    {
        _channelReader = tradeChannel.Reader;
        _webSocketServer = webSocketServer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TradeAggregatorService>.Instance;
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
    /// SPRINT-8: Batching loop - flush pending broadcasts every 200ms + send metadata every 2 seconds
    /// </summary>
    private async Task BatchBroadcastLoop(CancellationToken cancellationToken)
    {
        int tickCounter = 0;
        const int METADATA_BROADCAST_INTERVAL = 10; // SPRINT-8: Every 10 ticks (2 seconds at 200ms interval)

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _batchTimer.WaitForNextTickAsync(cancellationToken);
                tickCounter++;

                // 1. SPRINT-9: Send OHLCV aggregates (200ms timeframe) instead of individual trades
                if (!_pendingBroadcasts.IsEmpty)
                {
                    // SPRINT-R4: Zero-copy optimization - iterate directly over ConcurrentDictionary
                    foreach (var kvp in _pendingBroadcasts)
                    {
                        if (!_pendingBroadcasts.TryRemove(kvp.Key, out var trades)) continue;
                        if (trades == null || trades.Count == 0) continue;

                        // SPRINT-R4: Calculate everything inside lock to avoid ToList() copy
                        lock (trades)
                        {
                            if (trades.Count == 0) continue;

                            // SPRINT-R4: Single-pass calculation for buy/sell volumes (no ToList)
                            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            var open = trades[0].Price;
                            var close = trades[^1].Price;
                            decimal high = decimal.MinValue;
                            decimal low = decimal.MaxValue;
                            decimal totalVolume = 0;
                            decimal buyVolume = 0;
                            decimal sellVolume = 0;
                            int tradeCount = trades.Count;

                            foreach (var trade in trades)
                            {
                                if (trade.Price > high) high = trade.Price;
                                if (trade.Price < low) low = trade.Price;

                                var volume = trade.Price * trade.Quantity;
                                totalVolume += volume;

                                if (trade.Side.Equals("Buy", StringComparison.OrdinalIgnoreCase))
                                    buyVolume += volume;
                                else
                                    sellVolume += volume;
                            }

                            var message = new
                            {
                                type = "trade_aggregate",
                                symbol = kvp.Key,
                                aggregate = new
                                {
                                    timestamp = timestamp,
                                    open = open,
                                    high = high,
                                    low = low,
                                    close = close,
                                    volume = totalVolume,
                                    tradeCount = tradeCount,
                                    buyVolume = buyVolume,
                                    sellVolume = sellVolume
                                }
                            };

                            var json = JsonSerializer.Serialize(message);
                            _ = _webSocketServer.BroadcastRealtimeAsync(json);
                        }
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
                                trades5m = m.Trades5Min,  // SPRINT-10: for table sorting
                                // SPRINT-2: Advanced benchmarks
                                acceleration = m.Acceleration,
                                hasPattern = m.HasVolumePattern,
                                imbalance = m.BuySellImbalance,
                                compositeScore = m.CompositeScore,
                                lastPrice = m.LastPrice,
                                lastUpdate = ((DateTimeOffset)m.LastUpdate).ToUnixTimeMilliseconds(),
                                // SPRINT-10: 24h metrics for table
                                volume24h = m.Volume24h,
                                priceChangePercent24h = m.PriceChangePercent24h
                            })
                        };

                        var json = JsonSerializer.Serialize(batchMessage);
                        _ = _webSocketServer.BroadcastRealtimeAsync(json);

                        // SPRINT-2: Send TOP-70 list separately (for chart rendering on client)
                        // SPRINT-R4: Removed .ToList() - JSON serializer handles IEnumerable
                        var top70Message = new
                        {
                            type = "top70_update",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            symbols = allMetadata.Take(70).Select(m => m.Symbol)
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
    /// SPRINT-R3: Unified method to calculate trades within a time window (DRY principle)
    /// Replaces: CalculateTradesPerMinute, CalculateTrades2Min, CalculateTrades3Min
    /// </summary>
    private int CalculateTradesInWindow(string symbolKey, TimeSpan window)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var cutoff = DateTime.UtcNow - window;
        int count = 0;

        lock (queue)
        {
            foreach (var trade in queue)
            {
                if (trade.Timestamp >= cutoff)
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
    /// SPRINT-10: Update ticker data (Volume24h, PriceChangePercent24h) from exchange API
    /// Called periodically by OrchestrationService
    /// </summary>
    public void UpdateTickerData(IEnumerable<TickerData> tickers)
    {
        foreach (var ticker in tickers)
        {
            var key = $"MEXC_{ticker.Symbol}"; // Match trade data key format
            _tickerData.AddOrUpdate(key, ticker, (_, __) => ticker);
        }
    }

    /// <summary>
    /// SPRINT-3: Get metadata for all symbols sorted by trades/3m
    /// Simple sorting by activity - no complex composite scores
    /// </summary>
    public IEnumerable<SymbolMetadata> GetAllSymbolsMetadata()
    {
        // Calculate metrics for ALL symbols and sort by Trades3Min (simplest, clearest metric)
        return _symbolMetadata.Values
            .Select(m =>
            {
                var symbolKey = $"{(m.Symbol.StartsWith("MEXC_") ? "" : "MEXC_")}{m.Symbol}";

                // SPRINT-R3: Calculate rolling window metrics using unified method
                m.TradesPerMin = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(1));
                m.Trades2Min = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(2));
                m.Trades3Min = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(3));
                m.Trades5Min = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(5));  // SPRINT-10: for table sorting

                // Keep pump score for compatibility (but not used for sorting)
                m.Score = CalculatePumpScore(symbolKey, m.TradesPerMin);
                
                // SPRINT-2 benchmarks: Calculate for TOP-500 only (optimization)
                // Note: We calculate AFTER sorting by Trades3Min
                return m;
            })
            .OrderByDescending(m => m.Trades3Min)  // SPRINT-3: Sort by trades/3m - SIMPLE!
            .Select((m, index) =>
            {
                // Calculate advanced benchmarks only for TOP-500 (performance optimization)
                if (index < 500)
                {
                    var symbolKey = $"{(m.Symbol.StartsWith("MEXC_") ? "" : "MEXC_")}{m.Symbol}";
                    m.Acceleration = CalculateAcceleration(symbolKey, m.TradesPerMin, m.Trades2Min);
                    m.HasVolumePattern = DetectVolumePattern(symbolKey);
                    m.BuySellImbalance = CalculateBuySellImbalance(symbolKey);
                    m.CompositeScore = CalculateCompositeScore(m.Score, m.Acceleration, m.HasVolumePattern, m.BuySellImbalance);

                    // SPRINT-10: Populate 24h ticker data
                    if (_tickerData.TryGetValue(symbolKey, out var ticker))
                    {
                        m.Volume24h = ticker.Volume24h;
                        m.PriceChangePercent24h = ticker.PriceChangePercent24h;
                    }
                }
                return m;
            })
            .ToList();
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
    
    // SPRINT-7: Volume metrics
    public decimal Volume1Min { get; set; }         // USD volume in last 1 minute
    public decimal Volume3Min { get; set; }         // USD volume in last 3 minutes

    // SPRINT-10: Table view metrics
    public int Trades5Min { get; set; }             // Trades in last 5 minutes (for table sorting)
    public decimal Volume24h { get; set; }          // 24h volume from ticker
    public decimal PriceChangePercent24h { get; set; }  // 24h price change % from ticker
}
