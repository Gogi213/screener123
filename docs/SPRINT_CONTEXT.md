# 🚀 MEXC Trade Screener - Sprint Context

**Last Updated:** 2025-11-25 16:54 UTC+4  
**Status:** SPRINT-3 Completed ✅

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

---

### **SPRINT-3: Simple Sorting + TOP-30 Rendering** 

**Status:** ✅ COMPLETE  
**Duration:** ~2 hours (2025-11-25)

#### Goals Achieved:
1. ✅ **Server:** Sort by `trades3m` instead of composite score
2. ✅ **Client:** Render charts for TOP-30 (reduced from 50 for stability)
3. ✅ **Client:** Speed Sort (Smart Sort) working with trades3m
4. ✅ **Client:** Display changed from `/1m` → `/3m`
5. ✅ **BONUS:** Anti-flicker optimization - critical stability fix

#### Implementation:

**Server (C#) - `TradeAggregatorService.cs`:**
```csharp
// Simplified GetAllSymbolsMetadata() - removed complex CompositeScore logic
return _symbolMetadata.Values
    .Select(m => {
        // Calculate metrics
        m.TradesPerMin = CalculateTradesPerMinute(symbolKey);
        m.Trades2Min = CalculateTrades2Min(symbolKey);
        m.Trades3Min = CalculateTrades3Min(symbolKey);
        return m;
    })
    .OrderByDescending(m => m.Trades3Min)  // SPRINT-3: Simple sort by trades/3m
    .ToList();
```
- **Change:** От сложной 3-ступенчатой сортировки (pumpScore → top500 benchmarks → compositeScore) к простой сортировке по `Trades3Min`
- **Benefit:** Проще, быстрее, понятнее

**Client (JS) - `screener.js`:**

1. **TOP-30 Rendering:**
```javascript
const top30 = allSymbols.slice(0, 30);  // Reduced from 50 to 30 for stability
top30.forEach(s => createCard(s.symbol, s.tradeCount));
```

2. **Receive trades3m from WebSocket:**
```javascript
allSymbols = msg.symbols
    .map(s => {
        symbolActivity.set(s.symbol, {
            trades3m: s.trades3m || 0,
            lastUpdate: Date.now()
        });
        return {
            symbol: s.symbol,
            trades3m: s.trades3m || 0,
            // ...
        };
    });
```

3. **Display /3m on cards:**
```javascript
statsEl.textContent = `${count}/3m`;  // Changed from /1m
```

4. **Smart Sort with trades3m:**
```javascript
allSymbols.sort((a, b) => {
    const actA = symbolActivity.get(a.symbol)?.trades3m || 0;
    const actB = symbolActivity.get(b.symbol)?.trades3m || 0;
    return actB - actA;
});
```

#### CRITICAL FIX: Anti-Flicker Optimization

**Problem:** Графики дребезжали даже при выключенной Smart Sort
- **Root cause:** `renderPage()` вызывался каждые 2 секунды при получении `all_symbols_scored`, уничтожая и пересоздавая все графики

**Solution:**
1. **First Load Flag:**
```javascript
let isFirstLoad = true;

if (msg.type === 'all_symbols_scored') {
    allSymbols = msg.symbols.filter(...).map(...);
    
    // ANTI-FLICKER: Only render on first load
    if (isFirstLoad) {
        renderPage();
        isFirstLoad = false;
        console.log('[Screener] Initial render complete. Flicker protection enabled.');
    }
}
```

2. **Smart Sort Interval:** 2000ms → **10000ms** (10 seconds)
```javascript
smartSortInterval = setInterval(reorderCardsWithoutDestroy, 10000);
```

**Result:**
- ✅ При **выключенной** Smart Sort - **0 мерцания** (графики рендерятся один раз)
- ✅ При **включенной** Smart Sort - пересортировка раз в 10 сек (комфортно для глаз)
- ✅ WebSocket стабилен, нет disconnect ошибок

#### Performance:
- **TOP-30 charts:** ~100-150ms рендер
- **Server CPU:** ~2% для 2000 символов
- **WebSocket:** Стабильное соединение
- **Memory:** Контролируемая (circular buffer в chartData)

---

## 🔨 Pending Sprints

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
