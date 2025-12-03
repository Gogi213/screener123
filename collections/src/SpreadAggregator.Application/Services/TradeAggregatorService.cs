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
    private readonly TimeSpan WINDOW_SIZE = TimeSpan.FromMinutes(2); // 2-minute rolling window
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

    // SPRINT-12: Orderbook data storage for spread calculation
    private readonly ConcurrentDictionary<string, (decimal bid, decimal ask)> _orderbookData = new();

    // SPRINT-14: Large prints (прострелы) tracking - trades > 5x average volume
    private readonly ConcurrentDictionary<string, Queue<LargePrint>> _largePrints = new();

    // Price breakthroughs tracking - rapid price movements >1% in 1-5 seconds
    private readonly ConcurrentDictionary<string, Queue<PriceBreakthrough>> _priceBreakthroughs = new();

    // Batching: accumulate trades per symbol before broadcast
    private readonly ConcurrentDictionary<string, List<TradeData>> _pendingBroadcasts = new();
    private readonly System.Threading.PeriodicTimer _batchTimer;

    // SPRINT-12: Orderbook refresh timer (every 30 seconds for active symbols)
    private readonly System.Threading.PeriodicTimer _orderbookTimer;
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
        _orderbookTimer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(30)); // SPRINT-12: Refresh orderbook every 30 seconds
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

        lock (PriceAlignmentService.ConcurrentAccessLock)
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

        // SPRINT-14: Detect large prints (прострелы)
        DetectLargePrint(trade);

        // Detect price breakthroughs (rapid price movements)
        DetectPriceBreakthrough(trade);

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
                                trades5m = m.Trades5Min,  // SPRINT-10: for table sorting
                                // SPRINT-2: Advanced benchmarks
                                acceleration = m.Acceleration,
                                hasPattern = m.HasVolumePattern,
                                imbalance = m.BuySellImbalance,
                                compositeScore = m.CompositeScore,
                                lastPrice = m.LastPrice,
                                lastUpdate = ((DateTimeOffset)m.LastUpdate).ToUnixTimeMilliseconds(),
                                // SPRINT-10: Table metrics (24h volume + 4h price change)
                                volume24h = m.Volume24h,
                                priceChangePercent4h = m.PriceChangePercent4h,
                                // SPRINT-12: Spread metrics
                                spreadPercent = m.SpreadPercent,
                                spreadAbsolute = m.SpreadAbsolute,
                                // SPRINT-11: NATR indicator
                                natr = m.NATR,
                                // SPRINT-14: Large prints tracking
                                largePrintCount5m = m.LargePrintCount5m,
                                lastLargePrintRatio = m.LastLargePrintRatio,
                                // Price breakthroughs tracking
                                priceBreakthroughCount5m = m.PriceBreakthroughCount5m,
                                lastPriceBreakthroughChange = m.LastPriceBreakthroughChange
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
    /// Replaces: CalculateTradesPerMinute, CalculateTrades2Min, CalculateTrades5Min
    /// </summary>
    private int CalculateTradesInWindow(string symbolKey, TimeSpan window)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var cutoff = DateTime.UtcNow - window;
        int count = 0;

        lock (PriceAlignmentService.ConcurrentAccessLock)
        {
            var trades = queue.ToList();
            foreach (var trade in trades)
            {
                if (trade.Timestamp >= cutoff)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// SPRINT-12: Calculate spread from orderbook data
    /// FUTURES_FIX: Fallback to ticker.BestBid/BestAsk if orderbook not available
    /// </summary>
    private (decimal spreadPercent, decimal spreadAbsolute) CalculateSpread(string symbolKey)
    {
        // Primary source: orderbook data (for MEXC Spot - updated via GetOrderbookForSymbolsAsync)
        if (_orderbookData.TryGetValue(symbolKey, out var orderbook))
        {
            var (bid, ask) = orderbook;
            if (ask > 0 && bid > 0)
            {
                var spreadAbs = ask - bid;
                var spreadPct = (spreadAbs / ask) * 100;
                return (spreadPct, spreadAbs);
            }
        }

        // Fallback: ticker data (for MEXC Futures - BestBid/BestAsk already in ticker)
        if (_tickerData.TryGetValue(symbolKey, out var ticker))
        {
            if (ticker.BestAsk > 0 && ticker.BestBid > 0)
            {
                var spreadAbs = ticker.BestAsk - ticker.BestBid;
                var spreadPct = (spreadAbs / ticker.BestAsk) * 100;
                return (spreadPct, spreadAbs);
            }
        }

        return (0, 0);
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

        lock (PriceAlignmentService.ConcurrentAccessLock)
        {
            // Calculate USD volume in the last minute
            var trades = queue.ToList();
            foreach (var trade in trades)
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

        lock (PriceAlignmentService.ConcurrentAccessLock)
        {
            var trades = queue.ToList();
            recentTrades.AddRange(trades.Where(t => t.Timestamp >= oneMinuteAgo));
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

        lock (PriceAlignmentService.ConcurrentAccessLock)
        {
            var trades = queue.ToList();
            foreach (var trade in trades)
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
    /// FUTURES_FIX: Also ensures symbols exist in metadata (for exchanges without trades)
    /// </summary>
    public void UpdateTickerData(IEnumerable<TickerData> tickers, string exchangeName)
    {
        int updateCount = 0;
        foreach (var ticker in tickers)
        {
            var key = $"{exchangeName}_{ticker.Symbol}"; // FUTURES_FIX: Use actual exchange name
            _tickerData.AddOrUpdate(key, ticker, (_, __) => ticker);

            // FUTURES_FIX: Ensure symbol exists in metadata (important for exchanges without trades yet)
            // This allows symbols to appear in UI even if no trades have occurred
            _symbolMetadata.AddOrUpdate(key,
                new SymbolMetadata
                {
                    Symbol = ticker.Symbol,
                    LastPrice = ticker.LastPrice,
                    LastUpdate = DateTime.UtcNow
                },
                (_, existing) =>
                {
                    existing.LastPrice = ticker.LastPrice;
                    existing.LastUpdate = DateTime.UtcNow;
                    return existing;
                });

            // Update count for verbose logging if needed
        }


    }

    /// <summary>
    /// SPRINT-12: Update orderbook data for spread calculation
    /// Called periodically for active symbols only
    /// </summary>
    public void UpdateOrderbookData(Dictionary<string, (decimal bid, decimal ask)> orderbookData, string exchangeName)
    {
        foreach (var kvp in orderbookData)
        {
            var key = $"{exchangeName}_{kvp.Key}"; // FUTURES_FIX: Use actual exchange name
            _orderbookData.AddOrUpdate(key, kvp.Value, (_, __) => kvp.Value);
        }
    }

    /// <summary>
    /// SPRINT-12: Get active symbols (those with recent trades) for orderbook updates
    /// Separated by activity level for different refresh frequencies
    /// </summary>
    public (List<string> highActivity, List<string> lowActivity) GetActiveSymbolsByActivity()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5); // Symbols with trades in last 5 minutes
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);

        var symbolActivities = _symbolTrades.Keys
            .Where(key => key.StartsWith("MEXC_"))
            .Select(key =>
            {
                if (_symbolTrades.TryGetValue(key, out var queue))
                {
                    lock (PriceAlignmentService.ConcurrentAccessLock)
                    {
                        var trades = queue.ToList();
                        var recentTrades = trades.Where(t => t.Timestamp >= cutoff).ToList();
                        var lastMinuteTrades = trades.Count(t => t.Timestamp >= oneMinuteAgo);
                        return new
                        {
                            Symbol = key.Replace("MEXC_", ""),
                            HasRecentTrades = recentTrades.Any(),
                            LastMinuteTradeCount = lastMinuteTrades
                        };
                    }
                }
                return null;
            })
            .Where(x => x?.HasRecentTrades == true)
            .ToList();

        var highActivity = symbolActivities
            .Where(x => x.LastMinuteTradeCount >= 30)
            .Select(x => x.Symbol)
            .Take(200) // Limit high activity symbols
            .ToList();

        var lowActivity = symbolActivities
            .Where(x => x.LastMinuteTradeCount < 30)
            .Select(x => x.Symbol)
            .Take(200) // Limit low activity symbols
            .ToList();

        return (highActivity, lowActivity);
    }

    /// <summary>
    /// SPRINT-3: Get metadata for all symbols sorted by trades/3m
    /// Simple sorting by activity - no complex composite scores
    /// </summary>
    public IEnumerable<SymbolMetadata> GetAllSymbolsMetadata()
    {
        // Calculate summary stats silently

        // FUTURES_FIX: Use original keys from dictionary instead of reconstructing them
        // This fixes issue where MexcFutures_* symbols were not appearing (key mismatch)
        var result = _symbolMetadata
            .Select(kvp =>
            {
                var symbolKey = kvp.Key;  // Use original key: MEXC_BTC_USDT or MexcFutures_BTC_USDT
                var m = kvp.Value;

                // SPRINT-R3: Calculate rolling window metrics using unified method
                m.TradesPerMin = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(1));
                m.Trades2Min = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(2));
                m.Trades5Min = CalculateTradesInWindow(symbolKey, TimeSpan.FromMinutes(5));  // SPRINT-10: for table sorting

                // Keep pump score for compatibility (but not used for sorting)
                m.Score = CalculatePumpScore(symbolKey, m.TradesPerMin);

                // SPRINT-2 benchmarks: Calculate for TOP-500 only (optimization)
                // Note: We calculate AFTER sorting by Trades5Min
                return (symbolKey, m);
            })
            .OrderByDescending(x => x.m.Trades5Min)  // SPRINT-3: Sort by trades/5m - SIMPLE!
            .ThenByDescending(x => {
                // FUTURES_FIX: Secondary sort by Volume24h for symbols with NO trades (Trades5Min = 0)
                // This ensures futures appear in TOP list even without WebSocket trades
                if (_tickerData.TryGetValue(x.symbolKey, out var ticker))
                {
                    return ticker.Volume24h;
                }
                return 0m;
            })
            .Select((x, index) =>
            {
                var symbolKey = x.symbolKey;  // Use original key
                var m = x.m;

                // SPRINT-12: Calculate spread for ALL symbols that have ticker data
                if (_tickerData.TryGetValue(symbolKey, out var ticker))
                {
                    // SPRINT-12: Calculate spread from orderbook data
                    var (spreadPct, spreadAbs) = CalculateSpread(symbolKey);
                    m.SpreadPercent = spreadPct;
                    m.SpreadAbsolute = spreadAbs;
                    m.Volume24h = ticker.Volume24h;  // Also populate volume for all symbols
                }


                // SPRINT-11: Calculate NATR for symbols with sufficient trade data
                m.NATR = CalculateNATR(symbolKey);

                // SPRINT-14: Calculate large print metrics
                m.LargePrintCount5m = GetLargePrintCount5m(symbolKey);

                // Get last large print ratio and timestamp
                if (_largePrints.TryGetValue(symbolKey, out var largePrintHistory))
                {
                    lock (largePrintHistory)
                    {
                        var lastPrint = largePrintHistory.LastOrDefault();
                        if (lastPrint != null)
                        {
                            m.LastLargePrintRatio = lastPrint.Ratio;
                            m.LastLargePrintTime = lastPrint.Timestamp;
                        }
                    }
                }

                // Calculate price breakthrough metrics
                m.PriceBreakthroughCount5m = GetPriceBreakthroughCount5m(symbolKey);

                // Get last price breakthrough change and timestamp
                if (_priceBreakthroughs.TryGetValue(symbolKey, out var breakthroughHistory))
                {
                    lock (breakthroughHistory)
                    {
                        var lastBreakthrough = breakthroughHistory.LastOrDefault();
                        if (lastBreakthrough != null)
                        {
                            m.LastPriceBreakthroughChange = lastBreakthrough.PriceChange;
                            m.LastPriceBreakthroughTime = lastBreakthrough.Timestamp;
                        }
                    }
                }

                // Calculate advanced benchmarks only for TOP-500 (performance optimization)
                if (index < 500)
                {
                    m.Acceleration = CalculateAcceleration(symbolKey, m.TradesPerMin, m.Trades2Min);
                    m.HasVolumePattern = DetectVolumePattern(symbolKey);
                    m.BuySellImbalance = CalculateBuySellImbalance(symbolKey);
                    m.CompositeScore = CalculateCompositeScore(m.Score, m.Acceleration, m.HasVolumePattern, m.BuySellImbalance);

                    // Calculate 4h price change from local trade data
                    m.PriceChangePercent4h = Calculate4hPriceChange(symbolKey);
                }
                return m;
            })
            .ToList();

        // Silent return - no debug logs

        return result;
    }

    /// <summary>
    /// SPRINT-12: Calculate spread (bid-ask difference)
    /// </summary>
    private (decimal spreadPercent, decimal spreadAbsolute) CalculateSpread(decimal bid, decimal ask)
    {
        if (ask <= 0 || bid <= 0 || bid >= ask) return (0, 0);

        var spreadAbs = ask - bid;
        var spreadPct = (spreadAbs / ask) * 100;

        return (spreadPct, spreadAbs);
    }

    /// <summary>
    /// Calculate 4-hour price change percentage using local trade data
    /// </summary>
    private decimal Calculate4hPriceChange(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        if (queue.Count < 2)
            return 0;

        var fourHoursAgo = DateTime.UtcNow.AddHours(-4);
        TradeData currentTrade = null;
        TradeData oldTrade = null;

        lock (PriceAlignmentService.ConcurrentAccessLock)
        {
            var trades = queue.ToList();  // Snapshot under lock
            
            // Get current (last) trade
            currentTrade = trades.LastOrDefault();
            if (currentTrade == null)
                return 0;

            // Find trade closest to 4 hours ago
            oldTrade = trades.FirstOrDefault(t => t.Timestamp >= fourHoursAgo);

            // If no trade found within 4h window, use oldest available
            if (oldTrade == null)
                oldTrade = trades.FirstOrDefault();

            if (oldTrade == null || oldTrade.Price == 0)
                return 0;
        }

        // Calculate percentage change
        var priceChange = ((currentTrade.Price - oldTrade.Price) / oldTrade.Price) * 100;

        // Clamp to reasonable range
        return Math.Max(-100, Math.Min(1000, priceChange));
    }

    /// <summary>
    /// SPRINT-11: Calculate NATR (Normalized Average True Range) for 10 periods over 1-minute timeframe
    /// Only calculates for symbols with >30 trades in last 10 minutes
    /// Formula: NATR = (ATR / Close) * 100 where ATR is 10-period EMA of TR
    /// </summary>
    private decimal CalculateNATR(string symbolKey)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return 0;

        var now = DateTime.UtcNow;
        var tenMinutesAgo = now.AddMinutes(-10);

        // Group trades into 1-minute candles
        var candles = new List<(decimal high, decimal low, decimal close, DateTime timestamp)>();

        lock (queue)
        {
            // Create snapshot to prevent "collection was modified" exception
            var snapshot = queue.ToArray();
            
            // Get trades from last 10 minutes
            var recentTrades = snapshot.Where(t => t.Timestamp >= tenMinutesAgo).ToList();

            // Filter: Only calculate for symbols with >30 trades in last 10 minutes
            if (recentTrades.Count <= 30)
            {
                _logger.LogDebug("[NATR] {Symbol}: Skipped - only {Count} trades in last 10min (<=30)", symbolKey, recentTrades.Count);
                return 0;
            }

            // Group by minute
            var groupedByMinute = recentTrades
                .GroupBy(t => new DateTime(t.Timestamp.Year, t.Timestamp.Month, t.Timestamp.Day,
                                          t.Timestamp.Hour, t.Timestamp.Minute, 0))
                .OrderBy(g => g.Key)
                .ToList();

            // Create candles (OHLC) for each minute
            foreach (var group in groupedByMinute)
            {
                var trades = group.OrderBy(t => t.Timestamp).ToList();
                if (trades.Count == 0) continue;

                var high = trades.Max(t => t.Price);
                var low = trades.Min(t => t.Price);
                var close = trades.Last().Price;
                var timestamp = group.Key;

                candles.Add((high, low, close, timestamp));
            }
        }

        // Need at least 10 candles for 10-period EMA
        if (candles.Count < 10)
        {
            _logger.LogDebug("[NATR] {Symbol}: Skipped - only {Count} candles (<10)", symbolKey, candles.Count);
            return 0;
        }

        // Calculate True Range for each candle
        var trueRanges = new List<decimal>();
        for (int i = 0; i < candles.Count; i++)
        {
            var (high, low, close, _) = candles[i];
            decimal prevClose = (i > 0) ? candles[i - 1].close : close;

            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trueRanges.Add(tr);
        }

        // Calculate 10-period EMA of True Range (ATR)
        if (trueRanges.Count < 10)
            return 0;

        decimal multiplier = 2.0m / (10 + 1); // EMA multiplier
        decimal atr = trueRanges[0]; // First value

        for (int i = 1; i < trueRanges.Count; i++)
        {
            atr = ((trueRanges[i] - atr) * multiplier) + atr;
        }

        // Get current close price
        var currentClose = candles.Last().close;
        if (currentClose <= 0)
            return 0;

        // Calculate NATR: (ATR / Close) * 100
        var natr = (atr / currentClose) * 100;

        // Clamp to reasonable range (0-100%)
        var clampedNatr = Math.Max(0, Math.Min(100, natr));
        _logger.LogDebug("[NATR] {Symbol}: Calculated NATR = {Natr:F2}% (ATR={Atr:F6}, Close={Close:F6})", symbolKey, clampedNatr, atr, currentClose);
        return clampedNatr;
    }

    /// <summary>
    /// SPRINT-14: Detect large prints (прострелы) - trades > 5x average volume
    /// Called on every trade in ProcessTrade hot path
    /// </summary>
    private void DetectLargePrint(TradeData trade)
    {
        var symbolKey = $"{trade.Exchange}_{trade.Symbol}";

        // Calculate average trade volume (1-minute rolling window)
        var oneMinAgo = DateTime.UtcNow.AddMinutes(-1);
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return;

        decimal avgVolume = 0;
        int recentTradeCount = 0;

        lock (queue)
        {
            // Calculate average volume from recent trades
            foreach (var t in queue)
            {
                if (t.Timestamp >= oneMinAgo)
                {
                    avgVolume += t.Price * t.Quantity;
                    recentTradeCount++;
                }
            }
        }

        // Need minimum 10 trades for baseline
        if (recentTradeCount < 10)
            return;

        avgVolume /= recentTradeCount;
        var currentVolume = trade.Price * trade.Quantity;

        // Threshold: 4x average trade volume AND minimum $200 AND price change < 1%
        const decimal THRESHOLD = 4.0m;
        const decimal MIN_VOLUME = 200.0m;
        const decimal MAX_PRICE_CHANGE = 1.0m; // To avoid detecting breakthroughs as large prints

        var ratio = avgVolume > 0 ? currentVolume / avgVolume : 0;

        // Check if this trade caused significant price movement (might be a breakthrough instead)
        var priceChange = 0.0m;
        if (recentTradeCount >= 2)
        {
            // Get previous trade price
            TradeData prevTrade = null;
            lock (queue)
            {
                foreach (var t in queue)
                {
                    if (t.Timestamp < trade.Timestamp)
                    {
                        prevTrade = t;
                    }
                }
            }
            if (prevTrade != null)
            {
                priceChange = Math.Abs(((trade.Price - prevTrade.Price) / prevTrade.Price) * 100);
            }
        }

        if (ratio >= THRESHOLD && currentVolume >= MIN_VOLUME && priceChange < MAX_PRICE_CHANGE)
        {
            // Detected large print
            var largePrint = new LargePrint
            {
                Timestamp = trade.Timestamp,
                Price = trade.Price,
                Quantity = trade.Quantity,
                VolumeUSD = currentVolume,
                Side = trade.Side,
                AvgTradeVolume = avgVolume,
                Ratio = ratio
            };

            // Store in history (auto-clean old on count)
            var history = _largePrints.GetOrAdd(symbolKey, _ => new Queue<LargePrint>());
            lock (history)
            {
                history.Enqueue(largePrint);
            }

            // Broadcast large print event
            BroadcastLargePrint(symbolKey, largePrint);

            _logger.LogInformation("[LargePrint] {Symbol}: {Ratio:F1}x {Side} @ {Price:F6} (Vol: ${Volume:F2}, Avg: ${Avg:F2})",
                trade.Symbol, ratio, trade.Side, trade.Price, currentVolume, avgVolume);
        }
    }

    /// <summary>
    /// SPRINT-14: Broadcast large print event to all WebSocket clients
    /// </summary>
    private void BroadcastLargePrint(string symbolKey, LargePrint print)
    {
        var symbol = symbolKey.Contains('_') ? symbolKey.Split('_')[1] : symbolKey;

        var msg = new
        {
            type = "large_print",
            symbol = symbol,
            timestamp = ((DateTimeOffset)print.Timestamp).ToUnixTimeMilliseconds(),
            price = print.Price,
            quantity = print.Quantity,
            volumeUSD = print.VolumeUSD,
            side = print.Side,
            ratio = print.Ratio,
            avgVolume = print.AvgTradeVolume
        };

        var json = JsonSerializer.Serialize(msg);
        _ = _webSocketServer.BroadcastRealtimeAsync(json);
    }

    /// <summary>
    /// Detect price breakthroughs - rapid price movements >1% in 2 seconds
    /// </summary>
    private void DetectPriceBreakthrough(TradeData trade)
    {
        var symbolKey = $"{trade.Exchange}_{trade.Symbol}";

        // Look at price 2 seconds ago
        var window = TimeSpan.FromSeconds(2);
        var cutoff = DateTime.UtcNow - window;

        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return;

        List<TradeData> recentTrades;
        lock (queue)
        {
            recentTrades = queue.Where(t => t.Timestamp >= cutoff).OrderBy(t => t.Timestamp).ToList();
        }

        if (recentTrades.Count < 3) return; // Need minimum trades

        var startPrice = recentTrades.First().Price;
        var currentPrice = trade.Price;

        // Calculate price change %
        var priceChange = ((currentPrice - startPrice) / startPrice) * 100;

        // Threshold: price changed >1% in 2 seconds
        const decimal BREAKTHROUGH_THRESHOLD = 1.0m;

        if (Math.Abs(priceChange) >= BREAKTHROUGH_THRESHOLD)
        {
            // Calculate volume in breakthrough
            var volumeInBreakthrough = recentTrades.Sum(t => t.Price * t.Quantity);

            // Calculate average trade volume (1-minute rolling window)
            var oneMinAgo = DateTime.UtcNow.AddMinutes(-1);
            decimal avgVolume = 0;
            int recentTradeCount = 0;

            lock (queue)
            {
                // Calculate average volume from recent trades
                foreach (var t in queue)
                {
                    if (t.Timestamp >= oneMinAgo)
                    {
                        avgVolume += t.Price * t.Quantity;
                        recentTradeCount++;
                    }
                }
            }

            // Need minimum 10 trades for baseline
            if (recentTradeCount < 10)
                return;

            avgVolume /= recentTradeCount;

            // Volume threshold: 300% above average AND minimum $500
            const decimal VOLUME_THRESHOLD_MULTIPLIER = 3.0m;
            const decimal MIN_VOLUME_THRESHOLD = 500.0m;
            var volumeThreshold = Math.Max(avgVolume * VOLUME_THRESHOLD_MULTIPLIER, MIN_VOLUME_THRESHOLD);

            if (volumeInBreakthrough >= volumeThreshold)
            {
                // This is a PRICE BREAKTHROUGH!
                var breakthrough = new PriceBreakthrough
                {
                    Timestamp = trade.Timestamp,
                    StartPrice = startPrice,
                    EndPrice = currentPrice,
                    PriceChange = priceChange,
                    TimeSpan = (decimal)window.TotalSeconds,
                    Direction = priceChange > 0 ? "Up" : "Down",
                    VolumeInBreakthrough = volumeInBreakthrough
                };

                // Store in history (auto-clean old on count)
                var history = _priceBreakthroughs.GetOrAdd(symbolKey, _ => new Queue<PriceBreakthrough>());
                lock (history)
                {
                    history.Enqueue(breakthrough);
                }

                // Broadcast breakthrough event
                BroadcastPriceBreakthrough(symbolKey, breakthrough);

                _logger.LogInformation("[PriceBreakthrough] {Symbol}: {Direction} {PriceChange:F2}% in {TimeSpan}s (Start: ${StartPrice:F6}, End: ${EndPrice:F6}, Vol: ${Volume:F2}, Avg: ${Avg:F2}, Ratio: {Ratio:F1}x)",
                    trade.Symbol, breakthrough.Direction, priceChange, window.TotalSeconds, startPrice, currentPrice, volumeInBreakthrough, avgVolume, volumeInBreakthrough / avgVolume);
            }
        }
    }

    /// <summary>
    /// Broadcast price breakthrough event to all WebSocket clients
    /// </summary>
    private void BroadcastPriceBreakthrough(string symbolKey, PriceBreakthrough breakthrough)
    {
        var symbol = symbolKey.Contains('_') ? symbolKey.Split('_')[1] : symbolKey;

        var msg = new
        {
            type = "price_breakthrough",
            symbol = symbol,
            timestamp = ((DateTimeOffset)breakthrough.Timestamp).ToUnixTimeMilliseconds(),
            startPrice = breakthrough.StartPrice,
            endPrice = breakthrough.EndPrice,
            priceChange = breakthrough.PriceChange,
            timeSpan = breakthrough.TimeSpan,
            direction = breakthrough.Direction,
            volumeInBreakthrough = breakthrough.VolumeInBreakthrough
        };

        var json = JsonSerializer.Serialize(msg);
        _ = _webSocketServer.BroadcastRealtimeAsync(json);
    }

    /// <summary>
    /// SPRINT-14: Get count of large prints in last 1.5 minutes for a symbol (auto-clean old)
    /// </summary>
    private int GetLargePrintCount5m(string symbolKey)
    {
        if (!_largePrints.TryGetValue(symbolKey, out var history))
            return 0;

        var oneAndHalfMinAgo = DateTime.UtcNow.AddMinutes(-1.5);
        int count = 0;

        lock (history)
        {
            // Remove prints older than 1.5 minutes
            while (history.Count > 0 && history.Peek().Timestamp < oneAndHalfMinAgo)
            {
                history.Dequeue();
            }

            // Count remaining (all are within 1.5 minutes)
            count = history.Count;
        }

        return count;
    }

    /// <summary>
    /// Get count of price breakthroughs in last 5 minutes for a symbol (auto-clean old)
    /// </summary>
    private int GetPriceBreakthroughCount5m(string symbolKey)
    {
        if (!_priceBreakthroughs.TryGetValue(symbolKey, out var history))
            return 0;

        var fiveMinAgo = DateTime.UtcNow.AddMinutes(-5);
        int count = 0;

        lock (history)
        {
            // Remove breakthroughs older than 5 minutes
            while (history.Count > 0 && history.Peek().Timestamp < fiveMinAgo)
            {
                history.Dequeue();
            }

            // Count remaining (all are within 5 minutes)
            count = history.Count;
        }

        return count;
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
    public int Trades5Min { get; set; }    // Trades in last 5 minutes (for trend analysis)
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
    public decimal Volume24h { get; set; }          // 24h volume from ticker
    public decimal PriceChangePercent4h { get; set; }  // 4h price change % calculated from local trades

    // SPRINT-12: Spread metrics
    public decimal SpreadPercent { get; set; }      // Spread percentage ((ask-bid)/ask * 100)
    public decimal SpreadAbsolute { get; set; }     // Absolute spread (ask - bid)

    // SPRINT-11: NATR (Normalized Average True Range) indicator
    public decimal NATR { get; set; }               // NATR percentage (10-period EMA of TR / Close * 100)

    // SPRINT-14: Large prints tracking
    public int LargePrintCount5m { get; set; }      // Count of large prints in last 5 minutes
    public decimal LastLargePrintRatio { get; set; } // Most recent large print ratio (e.g., 8.5x)
    public DateTime? LastLargePrintTime { get; set; } // Timestamp of last large print

    // Price breakthroughs tracking
    public int PriceBreakthroughCount5m { get; set; }      // Count of breakthroughs in last 5 minutes
    public decimal LastPriceBreakthroughChange { get; set; } // Most recent breakthrough change %
    public DateTime? LastPriceBreakthroughTime { get; set; } // Timestamp of last breakthrough
}

/// <summary>
/// SPRINT-14: Large Print (Прострел) - individual trade significantly larger than average
/// </summary>
public class LargePrint
{
    public DateTime Timestamp { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal VolumeUSD { get; set; }
    public string Side { get; set; } = string.Empty; // "Buy" or "Sell"
    public decimal AvgTradeVolume { get; set; }
    public decimal Ratio { get; set; } // VolumeUSD / AvgTradeVolume (e.g., 5.0 = 500%)
}

/// <summary>
/// Price Breakthrough - rapid price movement in short timeframe (>1% in 1-5 seconds)
/// </summary>
public class PriceBreakthrough
{
    public DateTime Timestamp { get; set; }
    public decimal StartPrice { get; set; }
    public decimal EndPrice { get; set; }
    public decimal PriceChange { get; set; }      // % изменения
    public decimal TimeSpan { get; set; }         // секунды
    public string Direction { get; set; } = string.Empty;         // "Up" / "Down"
    public decimal VolumeInBreakthrough { get; set; }
}
