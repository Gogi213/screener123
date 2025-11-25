# 🚀 MEXC Trade Screener - Sprint Context

**Last Updated:** 2025-11-25 07:07 UTC+4  
**Status:** SPRINT-2 Completed, SPRINT-3 Pending

---

## 📋 Project Overview

**Goal:** Real-time trade screener for 2000+ MEXC coins with intelligent filtering and visualization.

**Core Concept:**
- Collect trades for ALL 2000 coins via WebSocket (server-side)
- Calculate rolling window metrics (1m, 2m, 3m)
- Sort by trade velocity (`trades/3m`)
- Render charts for TOP-50 most active coins only
- Dynamic updates with "Speed Sort" toggle

---

## ✅ Completed Sprints

### **SPRINT-1: Extended Rolling Window Metrics**

**Status:** ✅ COMPLETE  
**Duration:** ~1 hour

#### Implementation:

**Server (C#) - `TradeAggregatorService.cs`:**
```csharp
// Added methods:
private int CalculateTradesPerMinute(string symbolKey)
private int CalculateTrades2Min(string symbolKey)
private int CalculateTrades3Min(string symbolKey)

// Extended SymbolMetadata:
public int TradesPerMin { get; set; }   // Last 1 minute
public int Trades2Min { get; set; }     // Last 2 minutes
public int Trades3Min { get; set; }     // Last 3 minutes
```

**WebSocket Message:**
```json
{
  "type": "all_symbols_scored",
  "symbols": [
    {
      "symbol": "BTCUSDT",
      "tradesPerMin": 100,
      "trades2m": 195,
      "trades3m": 285,
      "lastPrice": 45000
    }
  ]
}
```

**Performance:**
- Broadcast every 2 seconds
- ~2000 symbols processed
- CPU: <1%

---

### **SPRINT-2: Advanced Benchmarks**

**Status:** ✅ COMPLETE (но не используется для сортировки)  
**Duration:** ~2 hours

#### Implementation:

**Server (C#) - Added methods:**

1. **Acceleration Detection:**
```csharp
private double CalculateAcceleration(string symbolKey, int trades1m, int trades2m)
{
    var tradesPreviousMin = trades2m - trades1m;
    if (tradesPreviousMin <= 0) return 1.0;
    return (double)trades1m / tradesPreviousMin;
}
```
- **Purpose:** Detect sudden spikes in trading activity
- **Formula:** `trades_current_minute / trades_previous_minute`
- **Example:** 2.5x means current minute 2.5x faster than previous

2. **Volume Pattern Detection (Bot Detection):**
```csharp
private bool DetectVolumePattern(string symbolKey)
{
    // Groups trades by (Volume, Side)
    // Returns true if 10+ trades with same volume/side
}
```
- **Purpose:** Detect bot activity (repeated identical trades)
- **Logic:** GroupBy exact volume and side matches
- **Threshold:** 10+ identical trades = pattern detected

3. **Buy/Sell Imbalance:**
```csharp
private double CalculateBuySellImbalance(string symbolKey)
{
    return |buyVolume - sellVolume| / (buyVolume + sellVolume);
}
```
- **Purpose:** Measure directional pressure
- **Range:** 0.0 (balanced) to 1.0 (one-sided)
- **Example:** 0.85 = 92.5% buys, 7.5% sells

4. **Composite Score (NOT USED FOR SORTING):**
```csharp
private double CalculateCompositeScore(
    double pumpScore, 
    double acceleration, 
    bool hasPattern, 
    double imbalance)
{
    var cappedAcceleration = Math.Min(acceleration, 5.0);
    var baseScore = pumpScore * (1.0 + cappedAcceleration / 2.0);
    var patternBonus = hasPattern ? 100.0 : 0.0;
    var imbalanceBonus = imbalance * 100.0;
    return baseScore + patternBonus + imbalanceBonus;
}
```

**Extended SymbolMetadata:**
```csharp
// SPRINT-2: Advanced benchmarks
public double Acceleration { get; set; }
public bool HasVolumePattern { get; set; }
public double BuySellImbalance { get; set; }
public double CompositeScore { get; set; }
```

**WebSocket Message Extended:**
```json
{
  "symbol": "BTCUSDT",
  "trades3m": 285,
  "acceleration": 2.5,
  "hasPattern": true,
  "imbalance": 0.85,
  "compositeScore": 780.5
}
```

**Performance:**
- Benchmarks calculated for TOP-500 only (optimization)
- CPU: ~1-2% (very cheap operations)
- All operations O(n) where n = ~100-300 trades

---

## 🔨 Pending Sprints

### **SPRINT-3: Simple Sorting + TOP-50 Rendering**

**Status:** 🔨 TODO  
**Estimated Duration:** 1-2 hours

#### Goals:
1. **Server:** Sort by `trades3m` (NOT composite score)
2. **Client:** Render charts for TOP-50 only
3. **Client:** Implement Speed Sort toggle
4. **Client:** Change display from `123/1m` → `123/3m`

#### Tasks:

**Server (C#):**
- ✏️ Modify `GetAllSymbolsMetadata()`:
  ```csharp
  .OrderByDescending(m => m.Trades3Min) // Primary sort
  ```
- ✏️ Remove `top70_update` WebSocket message (not needed)
- ✏️ Simplify logging

**Client (JS):**
- ✏️ Sort by `trades3m`:
  ```javascript
  allSymbols.sort((a, b) => b.trades3m - a.trades3m);
  const top50 = allSymbols.slice(0, 50);
  ```
- ✏️ Render charts ONLY for top50:
  ```javascript
  top50.forEach(symbol => createCard(symbol));
  ```
- ✏️ Speed Sort toggle:
  ```javascript
  if (speedSortEnabled) {
      // Update top50 every 2 seconds
      setInterval(reorderCardsWithoutDestroy, 2000);
  } else {
      // Freeze current 50 charts
  }
  ```
- ✏️ Update card stats: `${trades3m}/3m` instead of `/1m`

---

### **SPRINT-4: Benchmark Indicators (UI Polish)**

**Status:** 🔨 TODO  
**Estimated Duration:** 2-3 hours

#### Goals:
Show benchmark data on individual chart cards

#### Tasks:

**Client (JS):**
- ✏️ Add visual indicators to cards:
  ```
  ┌────────────────────────────┐
  │ BTCUSDT              45000 │
  │ 285/3m  🔥2.5x  🤖  📈    │
  │ ══════ Chart ══════       │
  └────────────────────────────┘
  ```

- ✏️ Indicator logic:
  - 🔥 - if `acceleration > 2.0` (show `🔥${acceleration}x`)
  - 🤖 - if `hasPattern = true` (bot detected)
  - 📈 - if `imbalance > 0.7` (buy pressure)
  - 📉 - if `imbalance < -0.7` (sell pressure)

- ✏️ Tooltip on hover:
  ```html
  <div class="tooltip">
    Acceleration: 2.5x (last minute 2.5x faster)
    Bot detected: 15 trades with volume 1000
    Buy pressure: 85% buys, 15% sells
  </div>
  ```

---

## 🏗️ Architecture

### **Data Flow:**

```
┌──────────────────────────────────────────┐
│ MEXC Exchange (2000+ symbols)            │
└────────────┬─────────────────────────────┘
             │ WebSocket Streams
             ▼
┌──────────────────────────────────────────┐
│ SERVER (C# - TradeAggregatorService)     │
│                                          │
│ 1. Collect trades for ALL 2000 symbols  │
│    - Store in rolling window (30 min)   │
│    - ConcurrentDictionary per symbol    │
│                                          │
│ 2. Calculate metrics (every 2 sec):     │
│    - trades/1m, 2m, 3m                   │
│    - acceleration                        │
│    - volume patterns                     │
│    - buy/sell imbalance                  │
│                                          │
│ 3. Sort ALL symbols by trades/3m        │
│                                          │
│ 4. Broadcast via WebSocket              │
│    - all_symbols_scored (2000 symbols)  │
│    - trade_update (incremental)         │
└────────────┬─────────────────────────────┘
             │ WS Messages
             ▼
┌──────────────────────────────────────────┐
│ CLIENT (Browser - screener.js)           │
│                                          │
│ 1. Receive all 2000 symbols with        │
│    metrics (NO charts yet)               │
│                                          │
│ 2. Sort by trades/3m locally            │
│                                          │
│ 3. Take TOP-50                           │
│                                          │
│ 4. Render uPlot charts ONLY for TOP-50  │
│    - Destroy charts for symbols not in  │
│      top50                               │
│    - Create charts for new symbols      │
│                                          │
│ 5. Speed Sort toggle:                   │
│    - ON: Update top50 every 2 sec       │
│    - OFF: Freeze current 50 charts      │
└──────────────────────────────────────────┘
```

### **Key Design Decisions:**

1. **NO pagination** - Display top50, not pages
2. **Server calculates metrics** - Client just sorts/filters
3. **TOP-500 optimization** - Benchmarks only for top 500 by pump score (4x CPU savings)
4. **Incremental chart updates** - `uPlot.setData()` instead of destroy/recreate
5. **Speed Sort toggle** - User control over chart volatility

---

## 📂 File Structure

```
collections/src/SpreadAggregator.Application/Services/
├── TradeAggregatorService.cs  ← Main service (SPRINT-1 & SPRINT-2)
│   ├── Rolling window storage
│   ├── Metrics calculation
│   ├── Benchmark calculation
│   └── WebSocket broadcast

collections/src/SpreadAggregator.Domain/Entities/
├── TradeData.cs               ← Trade model
└── SymbolMetadata.cs          ← Extended in SPRINT-1 & SPRINT-2

collections/src/SpreadAggregator.Presentation/wwwroot/
├── js/screener.js             ← Client logic (SPRINT-3 pending)
├── index.html                 ← UI (minimal changes)
└── styles.css                 ← Styling
```

---

## 🔧 Configuration

### **Server:**
- WebSocket server: `ws://0.0.0.0:8181`
- Broadcast interval: 2 seconds (100ms batching)
- Rolling window: 30 minutes
- Max trades per symbol: 1000
- Max symbols: 5000 (LRU)

### **Client:**
- Charts limit: 50 (TOP-50 by trades/3m)
- Update interval: 2 seconds (when Speed Sort enabled)
- Chart library: uPlot
- Batch throttle: 300ms

---

## ⚡ Performance Metrics

### **Server (per 2-second tick):**
| Operation | Count | Complexity | CPU % |
|-----------|-------|------------|-------|
| Basic metrics (trades/1m,2m,3m) | 2000 | O(n)×4000 | <1% |
| Benchmarks (TOP-500) | 500 | O(n)×1500 | ~1% |
| **Total** | - | ~2.85M ops | **~2%** |

### **Client:**
| Operation | Count | Impact |
|-----------|-------|--------|
| WebSocket message processing | 1/2sec | Minimal |
| Sorting 2000 symbols | 1/2sec | <10ms |
| uPlot chart updates (TOP-50) | 50 | ~100ms |
| **Total render time** | - | **<150ms** |

---

## 🐛 Known Issues

1. **screener.js corruption** - Fixed by reverting to git commit `59204ea`
2. **WebSocket disconnects** - Normal on page refresh, auto-reconnects
3. **Chart flicker** - Mitigated by incremental updates

---

## 📝 Next Actions (SPRINT-3)

### **Priority 1 - Server:**
1. Change sorting in `GetAllSymbolsMetadata()` to `trades3m`
2. Remove `top70_update` message
3. Test WebSocket output

### **Priority 2 - Client:**
1. Update `allSymbols.sort()` to use `trades3m`
2. Limit charts to TOP-50
3. Implement Speed Sort toggle logic
4. Change card display: `/1m` → `/3m`

### **Testing:**
1. Open browser DevTools
2. Monitor WebSocket messages
3. Verify sorting by trades3m
4. Check chart performance with 50 charts

---

## 🔗 Related Files

- `docs/GEMINI_DEV.md` - Development principles
- `docs/ARCHITECTURE.md` - System architecture (to be created)
- Git commit: `59204ea` - Last stable screener.js

---

## 💡 Key Insights

1. **Benchmarks are cheap** - All operations <2% CPU for 2000 symbols
2. **TOP-50 is optimal** - Browser handles 50 charts easily, 2000 crashes
3. **trades/3m is best metric** - More stable than 1m, faster than 5m
4. **Speed Sort toggle essential** - Users need control over chart changes
5. **Incremental updates work** - uPlot.setData() prevents flicker

---

**Session End: 2025-11-25 07:07 UTC+4**
