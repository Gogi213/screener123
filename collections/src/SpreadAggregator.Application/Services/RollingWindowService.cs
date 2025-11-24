using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Models;
using SpreadAggregator.Application.Helpers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.IO;
using SpreadAggregator.Application.Diagnostics;

namespace SpreadAggregator.Application.Services;

public class RollingWindowService : IDisposable
{
    // PROPOSAL-2025-0095: Memory safety - bounded collections with LRU eviction
    private const int MAX_WINDOWS = 10_000;
    private const int MAX_LATEST_TICKS = 50_000;

    private readonly ChannelReader<MarketData> _channelReader;
    private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(30); // Rolling window: 30 minutes (Screener mode)
    private readonly LruCache<string, RollingWindowData> _windows;
    private readonly Timer _cleanupTimer;
    private readonly Abstractions.IBidBidLogger? _bidBidLogger;
    private readonly ILogger<RollingWindowService> _logger;
    private bool _disposed;

    private readonly LruCache<string, TickData> _latestTicks;
    private readonly Timer _lastTickCleanupTimer;

    // PERFORMANCE: Index to quickly find which exchanges trade a symbol
    private readonly ConcurrentDictionary<string, HashSet<string>> _symbolExchanges = new();

    // TARGETED EVENTS: Index mapping "Exchange_Symbol" → ["WindowKey1", "WindowKey2", ...]
    private readonly ConcurrentDictionary<string, HashSet<string>> _exchangeSymbolIndex = new();
    
    // TARGETED EVENTS: Per-window event handlers
    private readonly ConcurrentDictionary<string, EventHandler<WindowDataUpdatedEventArgs>?> _windowEvents = new();

    private int _cleanupRunning = 0; // Atomic flag for cleanup

    [Obsolete("Use SubscribeToWindow instead for better performance.")]
    public event EventHandler<WindowDataUpdatedEventArgs>? WindowDataUpdated;

    public RollingWindowService(
        Channel<MarketData> channel,
        Abstractions.IBidBidLogger? bidBidLogger = null,
        ILogger<RollingWindowService>? logger = null,
        PerformanceMonitor? perfMonitor = null)
    {
        _channelReader = channel.Reader;
        _bidBidLogger = bidBidLogger;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RollingWindowService>.Instance;

        _windows = new LruCache<string, RollingWindowData>(MAX_WINDOWS);
        _latestTicks = new LruCache<string, TickData>(MAX_LATEST_TICKS);

        _cleanupTimer = new Timer(CleanupOldData, null, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)), TimeSpan.FromMinutes(5));
        _lastTickCleanupTimer = new Timer(CleanupStaleLastTicks, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RollingWindowService started, waiting for data...");
        await foreach (var data in _channelReader.ReadAllAsync(cancellationToken))
        {
            ProcessData(data);
        }
    }

    private void ProcessData(MarketData data)
    {
        if (data is SpreadData spreadData)
        {
            // 1. MATCHING
            ProcessLastTickMatching(spreadData);

            // 2. UPDATE LATEST TICKS
            var tickKey = GetTickKey(data.Exchange, data.Symbol);
            _latestTicks.AddOrUpdate(tickKey, new TickData
            {
                Timestamp = data.Timestamp,
                Bid = spreadData.BestBid,
                Ask = spreadData.BestAsk
            });

            // 3. UPDATE INDEX
            _symbolExchanges.AddOrUpdate(data.Symbol, 
                new HashSet<string> { data.Exchange }, 
                (key, set) => {
                    lock (set) { set.Add(data.Exchange); }
                    return set;
                });
        }
        else if (data is TradeData tradeData) // SCREENER: Handle trades
        {
            AddTradeToWindow(tradeData);
        }
    }

    private void ProcessLastTickMatching(SpreadData currentData)
    {
        var currentExchange = currentData.Exchange;
        var symbol = currentData.Symbol;
        var now = currentData.Timestamp;

        List<(string key, TickData tick)> otherExchangeTicks;

        // 1.1 INDEX LOOKUP
        if (!_symbolExchanges.TryGetValue(symbol, out var exchanges))
            return;

        lock (exchanges)
        {
            otherExchangeTicks = exchanges
                .Where(ex => ex != currentExchange)
                .Select(ex => 
                {
                    var key = $"{ex}_{symbol}";
                    return (key, tick: _latestTicks.TryGetValue(key, out var t) ? t : default(TickData?));
                })
                .Where(x => x.tick.HasValue)
                .Select(x => (x.key, x.tick!.Value))
                .ToList();
        }

        // 1.2 CALCULATION LOOP
        foreach (var (key, oppositeTick) in otherExchangeTicks)
        {
            var oppositeExchange = key.Split('_')[0];
            var staleness = now - oppositeTick.Timestamp;

            var (ex1, ex2) = string.Compare(currentExchange, oppositeExchange, StringComparison.Ordinal) < 0
                ? (currentExchange, oppositeExchange)
                : (oppositeExchange, currentExchange);
            
            var bid1 = ex1 == currentExchange ? currentData.BestBid : oppositeTick.Bid;
            var bid2 = ex2 == currentExchange ? currentData.BestBid : oppositeTick.Bid;
            
            if (bid1 > 0 && bid2 > 0)
            {
                var spread = ((bid1 / bid2) - 1) * 100;
                
                var spreadPoint = new SpreadPoint
                {
                    Timestamp = now,
                    Symbol = symbol,
                    Exchange1 = ex1,
                    Exchange2 = ex2,
                    BestBid = bid1,
                    BestAsk = bid2,
                    SpreadPercent = spread,
                    Staleness = staleness,
                    TriggeredBy = currentExchange
                };
                
                // 1.3 ADD TO WINDOW
                AddSpreadPointToWindow(spreadPoint);
            }
        }
    }

    private void AddSpreadPointToWindow(SpreadPoint spreadPoint)
    {
        var windowKey = $"{spreadPoint.Exchange1}_{spreadPoint.Exchange2}_{spreadPoint.Symbol}";

        RollingWindowData? window;
        
        // 1.3.1 DICT LOOKUP
        if (!_windows.TryGetValue(windowKey, out window) || window == null)
        {
            window = new RollingWindowData
            {
                Exchange = $"{spreadPoint.Exchange1}→{spreadPoint.Exchange2}",
                Symbol = spreadPoint.Symbol,
                WindowStart = spreadPoint.Timestamp - _windowSize,
                WindowEnd = spreadPoint.Timestamp
            };
            _windows.AddOrUpdate(windowKey, window);
            
            // TARGETED EVENTS: Populate index for efficient event routing
            var index1 = $"{spreadPoint.Exchange1}_{spreadPoint.Symbol}";
            var index2 = $"{spreadPoint.Exchange2}_{spreadPoint.Symbol}";
            
            _exchangeSymbolIndex.AddOrUpdate(index1,
                new HashSet<string> { windowKey },
                (k, set) => { lock (set) { set.Add(windowKey); } return set; });
            
            _exchangeSymbolIndex.AddOrUpdate(index2,
                new HashSet<string> { windowKey },
                (k, set) => { lock (set) { set.Add(windowKey); } return set; });
        }

        // 1.3.2 LIST ADD
        window.WindowEnd = spreadPoint.Timestamp;
        window.WindowStart = spreadPoint.Timestamp - _windowSize;

        var legacySpread = new SpreadData
        {
            Exchange = window.Exchange,
            Symbol = spreadPoint.Symbol,
            Timestamp = spreadPoint.Timestamp,
            BestBid = spreadPoint.BestBid,
            BestAsk = spreadPoint.BestAsk
        };

        // PERFORMANCE FIX: Sliding Window with Queue (O(1) removal)
        lock (window.Spreads)
        {
            window.Spreads.Enqueue(legacySpread);
            
            // Incremental cleanup: Remove old data immediately (Sliding Window)
            var threshold = spreadPoint.Timestamp - _windowSize;
            while (window.Spreads.Count > 0 && window.Spreads.Peek().Timestamp < threshold)
            {
                window.Spreads.Dequeue();
            }
            
            // Safety Cap: Prevent infinite growth if timestamps are weird
            while (window.Spreads.Count > 5000)
            {
                window.Spreads.Dequeue();
            }
        }

        OnWindowDataUpdated(spreadPoint.TriggeredBy, spreadPoint.Symbol);
    }

    // SCREENER: Add trade to rolling window
    private void AddTradeToWindow(TradeData trade)
    {
        var windowKey = $"{trade.Exchange}_{trade.Symbol}";
        
        RollingWindowData? window;
        if (!_windows.TryGetValue(windowKey, out window) || window == null)
        {
            window = new RollingWindowData
            {
                Exchange = trade.Exchange,
                Symbol = trade.Symbol,
                WindowStart = trade.Timestamp - _windowSize,
                WindowEnd = trade.Timestamp
            };
            _windows.AddOrUpdate(windowKey, window);
            
            _logger.LogInformation($"[RollingWindow] Created new trade window: {windowKey}");
        }
        
        // Update window time range
        window.WindowEnd = trade.Timestamp;
        window.WindowStart = trade.Timestamp - _windowSize;
        
        // Add trade to queue with sliding window cleanup
        lock (window.Trades)
        {
            window.Trades.Enqueue(trade);
            
            // Sliding window: remove trades older than 30 minutes
            var threshold = trade.Timestamp - _windowSize;
            int removedCount = 0;
            while (window.Trades.Count > 0 && window.Trades.Peek().Timestamp < threshold)
            {
                window.Trades.Dequeue();
                removedCount++;
            }
            
            // CRITICAL FIX: Reduced safety cap from 100K to 10K
            // Rationale: 30 min × 10 trades/sec = ~18K expected
            // Old: 100K × 1000 symbols = 100M trades (OOM risk!)
            // New: 10K × 1000 symbols = 10M trades (safe)
            while (window.Trades.Count > 10_000)
            {
                window.Trades.Dequeue();
                removedCount++;
            }
            
            if (removedCount > 0)
            {
                _logger.LogDebug($"[RollingWindow] Evicted {removedCount} old trades from {windowKey}");
            }
        }
        
        // Notify subscribers (for future WebSocket)
        OnTradeAdded(trade.Exchange, trade.Symbol, trade);
    }

    private string GetTickKey(string exchange, string symbol) => $"{exchange}_{symbol}";

    private void OnWindowDataUpdated(string exchange, string symbol)
    {
        // TARGETED EVENTS: Find affected windows and notify only their subscribers
        var indexKey = $"{exchange}_{symbol}";
        if (_exchangeSymbolIndex.TryGetValue(indexKey, out var affectedWindows))
        {
            var eventArgs = new WindowDataUpdatedEventArgs
            {
                Exchange = exchange,
                Symbol = symbol,
                Timestamp = DateTime.UtcNow
            };
            
            HashSet<string> windowsCopy;
            lock (affectedWindows)
            {
                windowsCopy = new HashSet<string>(affectedWindows);
            }
            
            foreach (var windowKey in windowsCopy)
            {
                if (_windowEvents.TryGetValue(windowKey, out var handler) && handler != null)
                {
                    handler.Invoke(this, eventArgs);
                }
            }
        }
        
        // LEGACY: Support old global event (marked as Obsolete)
        #pragma warning disable CS0618 // Type or member is obsolete
        WindowDataUpdated?.Invoke(this, new WindowDataUpdatedEventArgs
        {
            Exchange = exchange,
            Symbol = symbol,
            Timestamp = DateTime.UtcNow
        });
        #pragma warning restore CS0618
    }
    
    public void SubscribeToWindow(string symbol, string exchange1, string exchange2, 
        EventHandler<WindowDataUpdatedEventArgs> handler)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        _windowEvents.AddOrUpdate(windowKey, handler, (k, existing) => existing + handler);
    }
    
    public void UnsubscribeFromWindow(string symbol, string exchange1, string exchange2,
        EventHandler<WindowDataUpdatedEventArgs> handler)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        if (_windowEvents.TryGetValue(windowKey, out var existing) && existing != null)
        {
            _windowEvents[windowKey] = existing - handler;
        }
    }

    private void CleanupOldData(object? state)
    {
        // TASK 2: Prevent concurrent cleanup execution
        if (Interlocked.CompareExchange(ref _cleanupRunning, 1, 0) != 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await CleanupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RollingWindow] Error during cleanup");
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        });
    }

    private async Task CleanupAsync()
    {
        var now = DateTime.UtcNow;
        var threshold = now - _windowSize;
        
        // 1. Evict empty/old windows (fast)
        var removedCount = _windows.EvictWhere((key, window) => window.WindowEnd < threshold);

        // 2. Cleanup internal lists (slow - needs batching)
        int pointsRemoved = 0;
        var windowsSnapshot = _windows.Values.ToList(); // Snapshot to avoid locking dictionary
        int processedCount = 0;
        const int BATCH_SIZE = 100;

        foreach(var window in windowsSnapshot)
        {
            // Optional: Double check cap (fast O(1) check)
            if (window.Spreads.Count > 5000)
            {
                lock (window.Spreads)
                {
                    while (window.Spreads.Count > 5000) window.Spreads.Dequeue();
                }
            }
            
            processedCount++;
            
            // Yield every BATCH_SIZE windows to prevent ThreadPool starvation
            if (processedCount % BATCH_SIZE == 0)
            {
                await Task.Yield(); 
            }
        }

        if (removedCount > 0 || pointsRemoved > 0)
        {
            _logger.LogInformation($"[RollingWindow] Cleanup: removed {removedCount} windows, {pointsRemoved} points");
        }
    }

    private void CleanupStaleLastTicks(object? state)
    {
        var now = DateTime.UtcNow;
        var threshold = now - TimeSpan.FromMinutes(5);
        var removedCount = _latestTicks.EvictWhere((key, tick) => tick.Timestamp < threshold);
        if (removedCount > 0)
            _logger.LogInformation($"[RollingWindow] Cleanup last-ticks: removed {removedCount}");
    }

    public RollingWindowData? GetWindowData(string exchange, string symbol)
    {
        var key = $"{exchange}_{symbol}";
        _windows.TryGetValue(key, out var window);
        return window;
    }

    public IEnumerable<RollingWindowData> GetAllWindows() => _windows.Values.ToList();
    public int GetWindowCount() => _windows.Count;
    public int GetTotalSpreadCount() => _windows.Values.Sum(w => w.Spreads.Count);

    // SCREENER: Get trades for API
    public List<TradeData> GetTrades(string symbol, string exchange = "MEXC")
    {
        var windowKey = $"{exchange}_{symbol}";
        if (_windows.TryGetValue(windowKey, out var window))
        {
            lock (window.Trades)
            {
                return window.Trades.ToList();
            }
        }
        return new List<TradeData>();
    }

    // SCREENER: Event for WebSocket
    public event EventHandler<TradeAddedEventArgs>? TradeAdded;

    private void OnTradeAdded(string exchange, string symbol, TradeData trade)
    {
        TradeAdded?.Invoke(this, new TradeAddedEventArgs
        {
            Exchange = exchange,
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            Trade = trade
        });
    }

    public RealtimeChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
    {
        var windowKey = $"{exchange1}_{exchange2}_{symbol}";
        var window = _windows.TryGetValue(windowKey, out var w) ? w : null;

        if (window == null) return null;

        List<SpreadData> allSpreads;
        lock (window.Spreads)
        {
            if (window.Spreads.Count == 0) return null;
            allSpreads = window.Spreads.OrderBy(s => s.Timestamp).ToList();
        }

        var spreads = allSpreads.Select(s => s.BestAsk == 0 ? (double?)null : (double)(((s.BestBid / s.BestAsk) - 1) * 100)).ToList();
        var timestamps = allSpreads.Select(s => (double)new DateTimeOffset(s.Timestamp).ToUnixTimeMilliseconds()).ToList();

        // Calculate bands (simple moving average + stdev for demo)
        var upperBands = spreads.Select(s => s.HasValue ? s.Value + 0.1 : (double?)null).ToList();
        var lowerBands = spreads.Select(s => s.HasValue ? s.Value - 0.1 : (double?)null).ToList();

        // Return only last 100 points for chart
        var count = spreads.Count;
        var take = Math.Min(count, 100);
        var skip = count - take;

        var recentIndices = Enumerable.Range(skip, take);
        
        var epochTimestamps = recentIndices.Select(i => timestamps[i]).ToList();
        var chartSpreadValues = recentIndices.Select(i => spreads[i]).ToList();
        var chartUpperBands = recentIndices.Select(i => upperBands[i]).ToList();
        var chartLowerBands = recentIndices.Select(i => lowerBands[i]).ToList();

        return new RealtimeChartData
        {
            Symbol = symbol,
            Exchange1 = exchange1,
            Exchange2 = exchange2,
            Timestamps = epochTimestamps,
            Spreads = chartSpreadValues,
            UpperBand = chartUpperBands,
            LowerBand = chartLowerBands
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cleanupTimer?.Dispose();
        _lastTickCleanupTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Real-time chart data
/// </summary>
public class RealtimeChartData
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public required List<double> Timestamps { get; set; }
    public required List<double?> Spreads { get; set; }
    public required List<double?> UpperBand { get; set; }
    public required List<double?> LowerBand { get; set; }
}

/// <summary>
/// Event args for window data updates
/// </summary>
public class WindowDataUpdatedEventArgs : EventArgs
{
    public required string Exchange { get; set; }
    public required string Symbol { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// PROPOSAL-2025-0095: Lightweight tick data for latest ticks cache
/// </summary>
public struct TickData
{
    public DateTime Timestamp { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
}

/// <summary>
/// Event args for trade added
/// </summary>
public class TradeAddedEventArgs : EventArgs
{
    public required string Exchange { get; set; }
    public required string Symbol { get; set; }
    public DateTime Timestamp { get; set; }
    public required TradeData Trade { get; set; }
}
