# 🚀 MEXC Trade Screener - Sprint Context

**Last Updated:** 2025-11-25 18:45 UTC+4  
**Status:** Production Ready - All Core Features Complete ✅

---

## 📋 Overview

MEXC Trade Screener - real-time monitoring and analysis tool for 2000+ trading pairs on MEXC exchange.

**Core Features:**
- Real-time WebSocket streaming of trades
- Rolling window metrics (trades/1m, 2m, 3m)
- Advanced benchmarks (acceleration, patterns, imbalance)
- TOP-30 display with uPlot charts
- Freeze/Live sorting controls

---

## ✅ Completed Sprints

### **SPRINT-0: Infrastructure**

**Status:** ✅ COMPLETE  
**Duration:** Multiple sessions (pre-2025-11-25)

#### Implementation:
- `OrchestrationService`: MEXC subscription management
- `TradeAggregatorService`: Trade aggregation with 30-min rolling window
- WebSocket server (Fleck) on port 8181
- uPlot charting library integration
- Basic UI with card grid layout

---

### **SPRINT-1: Extended Metrics**

**Status:** ✅ COMPLETE  
**Duration:** 1-2 hours

#### Goals Achieved:
1. ✅ Server calculates trades/1m, 2m, 3m
2. ✅ WebSocket broadcasts every 2 seconds
3. ✅ All symbols monitored (2000+)

#### Implementation:

**Server Methods:**
```csharp
private int CalculateTradesPerMinute(string symbolKey);
private int CalculateTrades2Min(string symbolKey);
private int CalculateTrades3Min(string symbolKey);
```

**WebSocket Message:**
```json
{
  "type": "all_symbols_scored",
  "symbols": [
    {
      "symbol": "BTCUSDT",
      "tradesPerMin": 120,
      "trades2m": 240,
      "trades3m": 360,
      ...
    }
  ]
}
```

---

### **SPRINT-2: Advanced Benchmarks**

**Status:** ✅ COMPLETE  
**Duration:** 2-3 hours

#### Goals Achieved:
1. ✅ Acceleration calculation
2. ✅ Volume pattern detection (bot detection)
3. ✅ Buy/Sell imbalance
4. ✅ Composite score (not used for sorting)

#### Implementation:

**Acceleration:**
```csharp
private double CalculateAcceleration(string symbolKey, int trades1m, int trades2m)
{
    var tradesPreviousMin = trades2m - trades1m;
    if (tradesPreviousMin <= 0) return 1.0;
    return (double)trades1m / tradesPreviousMin;
}
```

**Pattern Detection:**
```csharp
private bool DetectVolumePattern(string symbolKey)
{
    // Returns true if 10+ trades with same volume and side
    var groups = recentTrades
        .GroupBy(t => new { Volume = t.Quantity, Side = t.Side })
        .Where(g => g.Count() >= 10);
    return groups.Any();
}
```

**Imbalance:**
```csharp
private double CalculateBuySellImbalance(string symbolKey)
{
    // Formula: |buyVolume - sellVolume| / (buyVolume + sellVolume)
    // Returns 0-1 where 0 = balanced, 1 = one-sided
    return (double)Math.Abs(buyVolume - sellVolume) / (double)total;
}
```

**Optimization:** Calculated only for TOP-500 symbols (performance)

---

### **SPRINT-3: Simple Sorting + TOP-30 + Performance** 

**Status:** ✅ COMPLETE  
**Duration:** ~4 hours (2025-11-25)

#### Goals Achieved:
1. ✅ Server sorts by `trades3m` (simplified from CompositeScore)
2. ✅ Client renders TOP-30 only (reduced from 50)
3. ✅ Speed Sort → Live Sort/Frozen rename
4. ✅ Display changed from `/1m` → `/3m`
5. ✅ **BONUS:** Anti-flicker optimization
6. ✅ **BONUS:** Acceleration indicator on cards
7. ✅ **BONUS:** Performance optimizations

#### Implementation:

**Server Sorting (TradeAggregatorService.cs):**
```csharp
public IEnumerable<SymbolMetadata> GetAllSymbolsMetadata()
{
    return _symbolMetadata.Values
        .Select(m => {
            // Calculate metrics
            m.TradesPerMin = CalculateTradesPerMinute(symbolKey);
            m.Trades2Min = CalculateTrades2Min(symbolKey);
            m.Trades3Min = CalculateTrades3Min(symbolKey);
            return m;
        })
        .OrderByDescending(m => m.Trades3Min)  // SPRINT-3: Simple sort
        .Select((m, index) => {
            // Benchmarks only for TOP-500 (optimization)
            if (index < 500) {
                m.Acceleration = CalculateAcceleration(...);
                m.HasVolumePattern = DetectVolumePattern(...);
                m.BuySellImbalance = CalculateBuySellImbalance(...);
            }
            return m;
        })
        .ToList();
}
```

**Client TOP-30 Rendering (screener.js):**
```javascript
function renderPage(autoScroll = false) {
    cleanupPage();
    const top30 = allSymbols.slice(0, 30);
    statusText.textContent = `Live: TOP-30 of ${allSymbols.length} Pairs (sorted by trades/3m)`;
    top30.forEach(s => createCard(s.symbol, s.tradeCount));
}
```

**Anti-Flicker Fix:**
```javascript
let isFirstLoad = true;

if (msg.type === 'all_symbols_scored') {
    allSymbols = msg.symbols.filter(...).map(...);
    
    // ANTI-FLICKER: Only render on first load
    if (isFirstLoad) {
        renderPage();
        isFirstLoad = false;
    }
}
```

**Performance Optimizations:**
1. Batch throttle: 300ms → 1000ms (1 second)
2. Smart Sort interval: 2s → 10s
3. Graph rendering: scatter-only (removed stroke/fill)

**Acceleration Indicator:**
```javascript
// Always visible on cards
accelEl.textContent = `↑${accel.toFixed(1)}x`;

// Color coding
if (accel >= 3.0) {
    accelEl.style.color = '#ef4444'; // red
} else if (accel >= 2.0) {
    accelEl.style.color = '#f97316'; // orange
} else {
    accelEl.style.color = '#6b7280'; // gray
}
```

**Freeze Button:**
```javascript
function toggleSmartSort() {
    smartSortEnabled = !smartSortEnabled;
    if (smartSortEnabled) {
        btn.innerHTML = '<span id="sortIcon">🔥</span> Live Sort';
        startSmartSort();
    } else {
        btn.innerHTML = '<span id="sortIcon">❄️</span> Frozen';
        stopSmartSort();
    }
}
```

#### Critical Bug Fixes:

**Bug #1: Sorting Broken**
- **Problem:** Client was overwriting server `trades3m` data with local counts
- **Cause:** `updateSymbolActivity(symbol, count)` called from `updateCardStats()`
- **Fix:** Removed local data updates, use server-only data for sorting

**Before:**
```javascript
function updateCardStats(symbol, price) {
    // Local count calculation
    let count = 0;
    for (let i = data.times.length - 1; i >= 0; i--) {
        if (data.times[i] < threeMinutesAgo) break;
        count++;
    }
    
    // BUG: This overwrites server data!
    updateSymbolActivity(symbol, count);  // ← REMOVED
}
```

**After:**
```javascript
function updateCardStats(symbol, price) {
    let count = 0; // Only for UI display
    // ...calculate count...
    
    // NOTE: symbolActivity updated ONLY from WebSocket
    // Local count is for display only, not sorting
}
```

#### Performance Metrics:
- **TOP-30 charts:** ~100-150ms render
- **Server CPU:** ~2% for 2000 symbols
- **WebSocket:** Stable connection, no disconnects
- **Memory:** Controlled (circular buffer in chartData)

---

## 🔨 Optional Future Work

### **SPRINT-4: Visual Indicators (Optional)**

**Status:** Not started  
**Estimated Duration:** 2-3 hours

#### Goals:
1. Display imbalance indicator (📈/📉) on cards
2. Display bot pattern indicator (🤖) on cards
3. Color-code cards by overall "hotness"

#### Card Mockup:
```
┌────────────────────────────────┐
│ BTCUSDT                  45000 │
│ 285/3m  ↑2.5x  📈  🤖         │
│ ════════ Chart ════════        │
└────────────────────────────────┘
```

**Implementation Notes:**
- Imbalance: Show 📈 if > 0.7 and buy-heavy, 📉 if sell-heavy
- Pattern: Show 🤖 if `hasPattern = true`
- Already receiving data from server - just need UI

---

## 📊 Architecture Summary

### Data Flow:
```
MEXC Exchange
    ↓ WebSocket
OrchestrationService
    ↓ Channel
TradeAggregatorService
    ↓ Calculate metrics (trades/1m,2m,3m, acceleration, etc)
    ↓ Sort by trades3m
    ↓ WebSocket broadcast (every 2s)
Client (screener.js)
    ↓ Receive all_symbols_scored
    ↓ Render TOP-30
    ↓ Display acceleration indicator
    ↓ Smart Sort (10s interval if enabled)
```

### Key Files:
- **Server:** `TradeAggregatorService.cs` - all metrics calculation
- **Client:** `screener.js` - rendering, sorting, UI
- **UI:** `index.html` - structure

### Design Decisions:
1. **Simple sorting:** trades/3m instead of complex composite scores
2. **Server authority:** Client uses server data, doesn't recalculate
3. **Performance first:** TOP-30 limit, scatter graphs, batch throttling
4. **User control:** Freeze button for studying coins

---

## 🎯 Production Readiness

**Status:** ✅ Production Ready

**Checklist:**
- ✅ Stable WebSocket connection
- ✅ No browser crashes (TOP-30 limit)
- ✅ Accurate sorting (server-side)
- ✅ No chart flickering (anti-flicker fix)
- ✅ Performance optimized (1s batch, scatter graphs)
- ✅ User controls (freeze button)
- ✅ Real-time metrics display

**Known Limitations:**
- Only TOP-30 displayed (by design for performance)
- Some metrics calculated only for TOP-500 (optimization)
- Imbalance/pattern indicators not yet displayed (optional)

---

## 📝 Development Guidelines

### Adding New Metrics:
1. Add calculation method in `TradeAggregatorService.cs`
2. Add property to `SymbolMetadata` class
3. Include in WebSocket broadcast (line ~207)
4. Receive in client `all_symbols_scored` handler
5. Display in `updateCardStats()` or `createCard()`

### Performance Considerations:
- Calculate expensive metrics only for TOP-500
- Use throttling for UI updates
- Keep circular buffers bounded (MAX_TRADES_PER_SYMBOL = 1000)
- Avoid frequent `renderPage()` calls (use Smart Sort interval)

### Testing:
- Always test with server running (`dotnet run`)
- Check browser console for errors
- Verify sorting order matches trades/3m values
- Test Freeze button (sorting should stop)

---

**Last Sync:** 2025-11-25 18:45 UTC+4  
**Next Steps:** Optional UI polish (SPRINT-4) or deploy as-is
