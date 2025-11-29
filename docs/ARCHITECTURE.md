# 🏗️ MEXC Trade Screener - Technical Architecture

**Version:** 2.0  
**Last Updated:** 2025-11-25

---

## 🎯 System Overview

Real-time cryptocurrency trading screener for MEXC exchange with intelligent filtering and visualization.

**Key Features:**
- Monitor 2000+ symbols simultaneously
- Rolling window metrics (1m, 2m, 3m)
- Advanced anomaly detection (acceleration, patterns, imbalance)
- TOP-50 visualization with dynamic updates
- User-controlled sorting (Speed Sort toggle)

---

## 📐 Architecture Diagram

```
┌───────────────────────────────────────────────────────────────┐
│                    MEXC Exchange API                          │
│                    (WebSocket Streams)                        │
└─────────────────────────┬─────────────────────────────────────┘
                          │
         ┌────────────────┴────────────────┐
         │                                 │
         ▼                                 ▼
┌──────────────────┐              ┌──────────────────┐
│  Trade Stream    │              │  Symbol Metadata │
│  (Real-time)     │              │  (2449 symbols)  │
└────────┬─────────┘              └────────┬─────────┘
         │                                 │
         └────────────────┬────────────────┘
                          │
                          ▼
         ┌────────────────────────────────┐
         │   OrchestrationService         │
         │   - Manages subscriptions      │
         │   - Filters by volume          │
         │   - Feeds TradeScreenerChannel │
         └────────────────┬───────────────┘
                          │
                          ▼
         ┌────────────────────────────────┐
         │   TradeScreenerChannel         │
         │   (BoundedChannel: 1M capacity)│
         └────────────────┬───────────────┘
                          │
                          ▼
         ┌────────────────────────────────────────────────┐
         │        TradeAggregatorService                  │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  Rolling Window Storage                  │ │
         │  │  - ConcurrentDictionary<symbol, Queue>   │ │
         │  │  - 30-minute window per symbol           │ │
         │  │  - Max 1000 trades per symbol            │ │
         │  └──────────────────────────────────────────┘ │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  Metrics Calculator (every 2 sec)        │ │
         │  │  - trades/1m, 2m, 3m                     │ │
         │  │  - acceleration                          │ │
         │  │  - volume patterns                       │ │
         │  │  - buy/sell imbalance                    │ │
         │  └──────────────────────────────────────────┘ │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  Batch Broadcaster                       │ │
         │  │  - 100ms batching                        │ │
         │  │  - all_symbols_scored (2 sec)            │ │
         │  │  - trade_update (real-time)              │ │
         │  └──────────────────────────────────────────┘ │
         └────────────────┬───────────────────────────────┘
                          │
                          ▼
         ┌────────────────────────────────┐
         │   FleckWebSocketServer         │
         │   (ws://0.0.0.0:8181)          │
         └────────────────┬───────────────┘
                          │
                          ▼
         ┌────────────────────────────────────────────────┐
         │             CLIENT (Browser)                   │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  WebSocket Client (screener.js)          │ │
         │  │  - Receives all_symbols_scored           │ │
         │  │  - Receives trade_update                 │ │
         │  └──────────────────────────────────────────┘ │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  Data Processing                         │ │
         │  │  - Sort by trades/3m                     │ │
         │  │  - Select TOP-50                         │ │
         │  │  - Speed Sort toggle logic               │ │
         │  └──────────────────────────────────────────┘ │
         │                                                │
         │  ┌──────────────────────────────────────────┐ │
         │  │  Chart Rendering (uPlot)                 │ │
         │  │  - Max 50 charts                         │ │
         │  │  - Incremental updates                   │ │
         │  │  - Circular buffer (2000 points)         │ │
         │  └──────────────────────────────────────────┘ │
         └────────────────────────────────────────────────┘
```

---

## 🔧 Component Details

### **1. OrchestrationService**

**Location:** `SpreadAggregator.Application/Services/OrchestrationService.cs`

**Responsibilities:**
- Initialize MEXC exchange client
- Subscribe to WebSocket streams
- Filter symbols by volume threshold
- Feed trades to TradeScreenerChannel

**Key Methods:**
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    // 1. Fetch all symbols from MEXC
    var tickers = await _mexcClient.GetAllTickers();
    
    // 2. Filter by volume
    var filtered = _volumeFilter.FilterSymbols(tickers);
    
    // 3. Subscribe to trade streams
    await _mexcClient.SubscribeToTrades(filtered, OnTradeReceived);
}

private void OnTradeReceived(TradeData trade)
{
    // Write to channel (non-blocking)
    _tradeScreenerChannel.Writer.TryWrite(trade);
}
```

**Configuration:**
- Volume filter: Minimum daily volume threshold
- Max symbols: 5000 (safety limit)
- Exchange: MEXC only

---

### **2. TradeAggregatorService**

**Location:** `SpreadAggregator.Application/Services/TradeAggregatorService.cs`

**Responsibilities:**
- Consume trades from channel
- Maintain rolling windows per symbol
- Calculate metrics and benchmarks
- Broadcast via WebSocket

**Data Structures:**
```csharp
// Rolling window storage
private readonly ConcurrentDictionary<string, Queue<TradeData>> _symbolTrades;

// Symbol metadata with metrics
private readonly ConcurrentDictionary<string, SymbolMetadata> _symbolMetadata;

// Batching for WebSocket
private readonly ConcurrentDictionary<string, List<TradeData>> _pendingBroadcasts;
```

**Key Algorithms:**

#### **Rolling Window Management:**
```csharp
private void ProcessTrade(TradeData trade)
{
    var queue = _symbolTrades.GetOrAdd(symbol, new Queue<TradeData>());
    
    lock (queue)
    {
        // Incremental expiry (O(k) where k = expired trades)
        while (queue.Count > 0 && IsExpired(queue.Peek()))
        {
            queue.Dequeue();
        }
        
        // Add new trade
        queue.Enqueue(trade);
        
        // Cap size (memory protection)
        if (queue.Count > MAX_TRADES_PER_SYMBOL)
        {
            queue.Dequeue();
        }
    }
}
```

#### **Metrics Calculation:**
```csharp
private int CalculateTrades3Min(string symbolKey)
{
    var queue = _symbolTrades[symbolKey];
    var cutoff = DateTime.UtcNow.AddMinutes(-3);
    
    lock (queue)
    {
        return queue.Count(t => t.Timestamp >= cutoff);
    }
}
```

**Performance:**
- Batch interval: 100ms
- Metadata broadcast: Every 2 seconds
- Trade updates: Real-time (batched at 100ms)
- CPU usage: ~2% for 2000 symbols

---

### **3. Benchmark Calculators**

**Location:** `TradeAggregatorService.cs` (private methods)

#### **A. Acceleration Detection**

**Purpose:** Detect sudden increase in trading activity

**Formula:**
```
acceleration = trades_current_minute / trades_previous_minute
```

**Implementation:**
```csharp
private double CalculateAcceleration(string symbolKey, int trades1m, int trades2m)
{
    var tradesPreviousMin = trades2m - trades1m;
    if (tradesPreviousMin <= 0) return 1.0;
    return (double)trades1m / tradesPreviousMin;
}
```

**Interpretation:**
- `1.0` = No change
- `2.0` = 2x faster (current minute has 2x more trades)
- `5.0+` = Extreme spike (capped at 5.0 in composite score)

---

#### **B. Volume Pattern Detection**

**Purpose:** Identify bot activity (repeated trades)

**Logic:**
```csharp
private bool DetectVolumePattern(string symbolKey)
{
    var recentTrades = GetTradesInLastMinute(symbolKey);
    
    var groups = recentTrades
        .GroupBy(t => new { Volume = t.Quantity, Side = t.Side })
        .Where(g => g.Count() >= 10);
    
    return groups.Any();
}
```

**Threshold:**
- 10+ trades with exact same volume + side = Pattern detected

**Use Case:**
- Market maker bots often use fixed volumes
- Wash trading detection
- Spoofing detection

---

#### **C. Buy/Sell Imbalance**

**Purpose:** Measure directional pressure

**Formula:**
```
imbalance = |buyVolume - sellVolume| / (buyVolume + sellVolume)
```

**Implementation:**
```csharp
private double CalculateBuySellImbalance(string symbolKey)
{
    var (buyVolume, sellVolume) = CalculateVolumes(symbolKey);
    var total = buyVolume + sellVolume;
    
    if (total == 0) return 0;
    return Math.Abs(buyVolume - sellVolume) / total;
}
```

**Interpretation:**
- `0.0` = Perfect balance (50/50)
- `0.5` = 75/25 split
- `0.7+` = Strong directional pressure
- `1.0` = Completely one-sided

**Use Case:**
- Pump detection (high buy imbalance)
- Dump detection (high sell imbalance)
- Accumulation/distribution phases

---

### **4. WebSocket Messages**

#### **Message Type: `all_symbols_scored`**

**Frequency:** Every 2 seconds

**Format:**
```json
{
  "type": "all_symbols_scored",
  "timestamp": 1732518299123,
  "total": 2358,
  "symbols": [
    {
      "symbol": "BTCUSDT",
      "score": 520.5,
      "tradesPerMin": 245,
      "trades2m": 480,
      "trades3m": 720,
      "acceleration": 1.02,
      "hasPattern": false,
      "imbalance": 0.15,
      "compositeScore": 530.2,
      "lastPrice": 45000.00,
      "lastUpdate": 1732518299000
    }
  ]
}
```

**Size:** ~400KB (2000 symbols × 200 bytes)

---

#### **Message Type: `trade_update`**

**Frequency:** Real-time (batched at 100ms)

**Format:**
```json
{
  "type": "trade_update",
  "symbol": "MEXC_BTCUSDT",
  "trades": [
    {
      "price": 45000.50,
      "quantity": 0.1,
      "side": "Buy",
      "timestamp": 1732518299123
    }
  ]
}
```

**Use Case:** Incremental chart updates

---

### **5. Client Architecture**

**Location:** `wwwroot/js/screener.js`

#### **State Management:**
```javascript
// Global state
let allSymbols = [];              // All 2000 symbols with metrics
const chartData = new Map();      // symbol → {times, buys, sells}
const activeCharts = new Map();   // symbol → uPlot instance
let smartSortEnabled = true;      // Speed Sort toggle
```

#### **Data Flow:**

1. **Receive WebSocket message:**
```javascript
globalWebSocket.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    
    if (msg.type === 'all_symbols_scored') {
        // Update allSymbols with metrics
        allSymbols = msg.symbols;
        
        // Re-sort and render
        renderTopSymbols();
    }
    
    if (msg.type === 'trade_update') {
        // Add to chartData (for all symbols)
        addTradeToChart(msg.symbol, msg.trades);
        
        // Update chart if visible
        updateChart(msg.symbol);
    }
};
```

2. **Sort and filter:**
```javascript
function renderTopSymbols() {
    // Sort by trades/3m
    allSymbols.sort((a, b) => b.trades3m - a.trades3m);
    
    // Take top 50
    const top50 = allSymbols.slice(0, 50);
    
    // Render charts
    top50.forEach(symbol => {
        if (!activeCharts.has(symbol)) {
            createChart(symbol);
        }
    });
    
    // Destroy charts not in top 50
    activeCharts.forEach((chart, symbol) => {
        if (!top50.includes(symbol)) {
            chart.destroy();
            activeCharts.delete(symbol);
        }
    });
}
```

3. **Speed Sort toggle:**
```javascript
function toggleSpeedSort() {
    smartSortEnabled = !smartSortEnabled;
    
    if (smartSortEnabled) {
        // Enable auto-refresh
        smartSortInterval = setInterval(renderTopSymbols, 2000);
    } else {
        // Freeze charts
        clearInterval(smartSortInterval);
    }
}
```

---

## 🚀 Performance Optimization

### **Server Optimizations:**

1. **TOP-500 Benchmark Calculation:**
```csharp
// Calculate expensive benchmarks only for top 500
var top500 = allSymbols
    .OrderByDescending(m => m.Score)
    .Take(500);

foreach (var m in top500)
{
    m.Acceleration = CalculateAcceleration(...);
    m.HasPattern = DetectVolumePattern(...);
    m.Imbalance = CalculateBuySellImbalance(...);
}
```
**Savings:** 4x CPU reduction (500 vs 2000)

2. **Incremental Expiry:**
```csharp
// O(k) instead of O(n) where k = expired trades
while (queue.Peek().Timestamp < cutoff)
{
    queue.Dequeue();
}
```

3. **Batching:**
- WebSocket messages batched at 100ms
- Reduces network overhead by 10x

---

### **Client Optimizations:**

1. **Circular Buffer:**
```javascript
// Keep max 2000 points per chart
if (data.times.length > 2000) {
    data.startIndex += 100;
}

// Periodic compaction
if (data.startIndex > 500) {
    data.times = data.times.slice(data.startIndex);
    data.startIndex = 0;
}
```

2. **Incremental Chart Updates:**
```javascript
// Don't destroy/recreate - just update data
uplot.setData([
    data.times.slice(data.startIndex),
    data.buys.slice(data.startIndex),
    data.sells.slice(data.startIndex)
]);
```

3. **Batched Rendering:**
```javascript
// Throttle to 300ms (requestAnimationFrame)
function batchingLoop() {
    if (now - lastUpdate > 300) {
        pendingUpdates.forEach(updateChart);
        pendingUpdates.clear();
    }
    requestAnimationFrame(batchingLoop);
}
```

---

## 📊 Scalability Analysis

### **Current Limits:**

| Resource | Limit | Reason |
|----------|-------|--------|
| Symbols monitored | 2000 | Exchange API limit |
| Charts rendered | 50 | Browser performance |
| Trades per symbol | 1000 | Memory constraint |
| Window size | 30 min | Disk I/O limit |
| WebSocket bandwidth | ~400 KB/2sec | Network bandwidth |

### **Bottlenecks:**

1. **Client rendering** (50+ charts = lag)
2. **WebSocket message size** (2000 symbols × 200 bytes)
3. **Memory usage** (2000 × 1000 trades × 50 bytes = 100 MB)

### **Future Improvements:**

1. **Pagination** - Load metrics for all, but paginate charts
2. **Compression** - gzip WebSocket messages (3x reduction)
3. **Server-side filtering** - Send only top 100 to client
4. **IndexedDB** - Store historical data locally

---

## 🔒 Security Considerations

1. **Rate Limiting:** WebSocket connections limited
2. **Input Validation:** All trade data validated
3. **Memory Caps:** LRU eviction prevents OOM
4. **CORS:** Configured for localhost only

---

## 📝 Code Quality

### **Testing Strategy:**

- **Unit Tests:** Benchmark calculations
- **Integration Tests:** WebSocket flow
- **Manual Tests:** Browser performance

### **Monitoring:**

- `SimpleMonitor` for CPU/Memory tracking
- WebSocket connection health checks
- Logging at key decision points

---

---


## 🎯 Planned Features - WhiplashBreakthrough Detection

### **Problem Statement**
Current PriceBreakthrough detects only directional moves (>1% net change). Doesn't capture "whiplash" breakouts:
- Price spikes sharply up/down then immediately returns ("bounce")
- Market makers dumping liquidity with reversal
- Oscillatory movements not directional

**Example:** Price goes +1.2% down, then back up -0.8% (net -0.4%) - current formula misses, but whiplash detected.

### **Proposed Solution: WhiplashBreakthrough Metric**

#### **Formula**
```csharp
// Intrabar volatility detection
decimal minPrice = recentTrades.Min(t => t.Price);
decimal maxPrice = recentTrades.Max(t => t.Price);
decimal avgPrice = recentTrades.Average(t => t.Price);
decimal volatility = ((maxPrice - minPrice) / avgPrice) * 100;

// Whiplash conditions
if (volatility >= 1.5m && volumeInBreakthrough >= volumeThreshold)
{
    // Whiplash detected!
}
```

#### **Key Differences from PriceBreakthrough**
- **Volatility instead of direction:** `(max-min)/avg` vs `end-start`
- **Captures bounces:** Even if net delta small
- **Lower volume threshold:** 2x vs 3x avg (whiplash often smaller dumps)

#### **Implementation Plan**
1. **Extend DetectPriceBreakthrough method** in TradeAggregatorService
2. **Add whiplash flag/metric** to SymbolMetadata
3. **Broadcast separate message:** `type: "whiplash_breakthrough"`
4. **Frontend alert/sound** for whiplash events

#### **Use Cases**
- **Liquidity traps:** Market makers create visual liquidity then pull it
- **Contrarian signals:** Bounce after dump = buy opportunity
- **High-frequency patterns:** Faster reaction needed

#### **Performance Considerations**
- Same window (5s extended)
- O(N) where N=trades in window
- Separate from PriceBreakthrough (less computation per trade)

### **Activation Timeline**
- Phase 1: Backend implementation + logging
- Phase 2: Frontend alerts + testing
- Phase 3: Integration with trading bot

---

**Last Updated:** 2025-11-25 07:07 UTC+4  
**Author:** Antigravity AI + User collaboration
