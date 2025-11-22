// PHASE 2: RollingWindowService.cs Changes
// File: SpreadAggregator.Application/Services/RollingWindowService.cs

// ============================================
// CHANGE 1: Update window size (Line 25)
// ============================================
private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(30); // Changed from 5 to 30


// ============================================
// CHANGE 2: Update ProcessData to handle TradeData (Line 132)
// ============================================
private void ProcessData(MarketData data)
{
    using var _ = _profiler.Measure("Total_ProcessData");
    _perfMonitor?.RecordEvent("ProcessData");

    if (data is SpreadData spreadData)
    {
        // Existing spread logic (keep if needed, or remove for screener-only mode)
        ProcessLastTickMatching(spreadData);
        
        var tickKey = GetTickKey(data.Exchange, data.Symbol);
        _latestTicks.AddOrUpdate(tickKey, new TickData
        {
            Timestamp = data.Timestamp,
            Bid = spreadData.BestBid,
            Ask = spreadData.BestAsk
        });
        
        _symbolExchanges.AddOrUpdate(data.Symbol, 
            new HashSet<string> { data.Exchange }, 
            (key, set) => {
                lock (set) { set.Add(data.Exchange); }
                return set;
            });
    }
    else if (data is TradeData tradeData) // NEW: Handle trades
    {
        AddTradeToWindow(tradeData);
    }
}


// ============================================
// CHANGE 3: NEW METHOD - AddTradeToWindow
// ============================================
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
        
        // Safety cap: prevent unbounded growth if timestamps are weird
        while (window.Trades.Count > 100_000)
        {
            window.Trades.Dequeue();
            removedCount++;
        }
        
        if (removedCount > 0)
        {
            _logger.LogDebug($"[RollingWindow] Evicted {removedCount} old trades from {windowKey}");
        }
    }
    
    // Notify subscribers (for WebSocket)
    OnTradeAdded(trade.Exchange, trade.Symbol);
}


// ============================================
// CHANGE 4: NEW METHOD - GetTrades (for API)
// ============================================
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


// ============================================
// CHANGE 5: NEW EVENT - TradeAdded (for WebSocket)
// ============================================
public event EventHandler<TradeAddedEventArgs>? TradeAdded;

private void OnTradeAdded(string exchange, string symbol)
{
    TradeAdded?.Invoke(this, new TradeAddedEventArgs
    {
        Exchange = exchange,
        Symbol = symbol,
        Timestamp = DateTime.UtcNow
    });
}


// ============================================
// CHANGE 6: NEW EVENT ARGS
// ============================================
public class TradeAddedEventArgs : EventArgs
{
    public required string Exchange { get; set; }
    public required string Symbol { get; set; }
    public DateTime Timestamp { get; set; }
}


// ============================================
// CHANGE 7: Update CleanupAsync for 30min window (Line 410)
// ============================================
private async Task CleanupAsync()
{
    using (_profiler.Measure("Cleanup_Async"))
    {
        _perfMonitor?.RecordEvent("Cleanup_Start");
        
        var now = DateTime.UtcNow;
        var threshold = now - _windowSize; // Now 30 minutes
        
        // Evict empty/old windows
        var removedCount = _windows.EvictWhere((key, window) => 
            window.WindowEnd < threshold || 
            (window.Spreads.Count == 0 && window.Trades.Count == 0)); // Check both queues
        
        _perfMonitor?.RecordEvent("Cleanup_End");
        
        if (removedCount > 0)
        {
            _logger.LogInformation($"[RollingWindow] Cleanup: removed {removedCount} windows");
        }
    }
}
