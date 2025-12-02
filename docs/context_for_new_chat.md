# Context for New Chat: Bid/Bid Deviation Charts

## 🎯 USER OBJECTIVE

Fix empty deviation charts in MEXC Trade Screener Pro.

**Expected behavior:**
- 9 real-time charts showing bid/bid price deviation between Binance Futures and MEXC Futures
- Charts update every 500ms with new deviation data points
- Visual line graph showing how deviation changes over 90-second rolling window

**Current behavior:**
- Charts are created (visible containers with headers)
- Data is flowing (console shows 54+ points buffered per symbol)
- BUT: Charts are EMPTY - no lines visible

---

## 📂 PROJECT STRUCTURE

```
screener123/collections/
├── src/
│   ├── SpreadAggregator.Application/
│   │   └── Services/
│   │       ├── DeviationAnalysisService.cs     ← Calculates deviations, broadcasts WebSocket
│   │       ├── PriceAlignmentService.cs        ← Aligns prices between exchanges
│   │       ├── TradeAggregatorService.cs       ← Aggregates trade data
│   │       └── OrchestrationService.cs         ← Blacklist filtering
│   ├── SpreadAggregator.Infrastructure/
│   │   └── Services/Exchanges/
│   │       ├── BinanceFuturesExchangeClient.cs ← 9-symbol whitelist
│   │       └── MexcFuturesExchangeClient.cs
│   └── SpreadAggregator.Presentation/
│       └── wwwroot/
│           ├── index.html                      ← WebSocket routing
│           └── js/
│               └── screener.js                 ← uPlot chart logic
└── docs/
    └── deviation_charts_issue_summary.md       ← Full analysis
```

---

## 🔑 CRITICAL CONTEXT

### 1. Symbol Filtering Chain
```
Binance API (200+ symbols)
  ↓
BinanceFuturesExchangeClient.cs (WHITELIST: 9 symbols) ✅
  ↓
OrchestrationService.cs (BLACKLIST: allows deviation symbols) ✅
  ↓
DeviationAnalysisService (9 symbols for charts) ✅
```

**Symbols:** BTC_USDT, ETH_USDT, SOL_USDT, ZEC_USDT, SUI_USDT, ASTER_USDT, DOGE_USDT, HYPE_USDT, LINK_USDT

### 2. Data Flow
```
Backend:
  TradeAggregatorService → stores TradeData in 30min queues
  PriceAlignmentService → aligns prices between exchanges (uses TradeData.Price)
  DeviationAnalysisService → calculates deviation, broadcasts every 500ms

Frontend:
  index.html ws.onmessage → routes "deviation_update" messages
  screener.js handleDeviationUpdate() → buffers data points (180 max)
  screener.js updateDeviationChart() → calls uPlot.setData()
```

### 3. Current Data Source Issue
**Charts named "Bid/Bid Ratio" but use:**
- `TradeData.Price` = last executed trade price (NOT bid!)

**Should use:**
- `TickerData.BestBid` = best bid price from orderbook (exists but not used)

### 4. Console Logs Confirm Data Flow Works
```
[Deviation] Received update with 9 deviations
[Deviation] BTC_USDT: dev=0.0006%, price1=86506.8, price2=86506.3
[Deviation] BTC_USDT: buffer has 54 points
[Chart Update] BTC_USDT: 54 data points
[Chart Update] BTC_USDT: timestamps[0]=1764632904.377, deviations[0]=0
[Chart Update] BTC_USDT: Chart updated!
```

---

## 🛠️ RECENT FIXES (Already Applied)

### ✅ Completed:
1. **Blacklist fix:** Removed BTC/ETH/SOL/DOGE/SUI/LINK from blacklist in `OrchestrationService.cs:171`
2. **Thread-safety:** Fixed Calculate4hPriceChange to use ConcurrentAccessLock
3. **Timestamp uniqueness:** Changed from `msg.timestamp` to `Date.now()` in `screener.js:33`
4. **uPlot CDN:** Fixed 404 error by using single CDN source (`cdn.jsdelivr.net`)
5. **Debug logging:** Added throughout data flow pipeline

### ⚠️ NOT Fixed:
1. **Empty charts** - root cause still unknown
2. **Data source mismatch** - using TradeData.Price instead of BestBid
3. **Canvas rendering** - not verified if uPlot actually draws to canvas

---

## 🔍 INVESTIGATION NEEDED

### Priority 1: Canvas Dimensions
```js
// Add to createDeviationChart() in screener.js
console.log(`[Canvas] ${symbol}: width=${card.clientWidth}, height=150`);
console.log(`[Canvas] ${symbol}: uPlot instance=`, deviationCharts[symbol]);
```

**Check in DevTools:**
- Elements tab → find `<div id="uplot-BTC_USDT">`
- Verify `<canvas>` element exists
- Check canvas width/height attributes (should NOT be 0x0)

### Priority 2: Y-axis Range
Current: `-0.5%` to `+0.5%`  
Actual deviations: `~0.0006%`

**Hypothesis:** Line too thin to see at this scale

**Test:** Adjust to tighter range:
```js
scales: {
    y: { range: [-0.05, 0.05] }  // 10x zoom
}
```

### Priority 3: Data Source
**Current architecture uses WRONG data:**
- `PriceAlignmentService` → queries `TradeData` queues
- `TradeData.Price` = last trade (could be bid or ask)

**Should use:**
- `TickerData.BestBid` (updated every 10s via ticker refresh)
- Requires modifying `PriceAlignmentService` to track bid prices separately

---

## 🎨 Frontend Code Structure

### screener.js Key Functions:
```js
// Called by index.html when deviation_update message arrives
window.handleDeviationUpdate(msg) {
    // Adds data point: { timestamp: Date.now(), deviation, price1, price2 }
    // Updates chart via updateDeviationChart()
}

// Called by index.html when all_symbols_scored arrives
window.handleAllSymbolsScored(msg) {
    // Creates charts for 9 symbols
}

// Creates uPlot instance
createDeviationChart(symbol) {
    // Config: width=card.clientWidth, height=150
    // Scales: x=time, y=[-0.5, 0.5]
    // Series: line, green stroke
}

// Updates chart data
updateDeviationChart(symbol) {
    // Prepares: timestamps = data.map(d => d.timestamp / 1000)
    // Calls: deviationCharts[symbol].setData([timestamps, deviations])
}
```

### index.html WebSocket Routing:
```js
ws.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    
    if (msg.type === 'deviation_update' && chartsInitialized && window.handleDeviationUpdate) {
        window.handleDeviationUpdate(msg);  // ✅ This works
    }
}
```

---

## 📊 Known Good State

### Working Components:
- ✅ WebSocket connection established
- ✅ Backend sends deviation_update every 500ms
- ✅ Frontend receives messages (verified in console)
- ✅ Data buffering works (54+ points per symbol)
- ✅ uPlot library loaded (no 404 errors)
- ✅ Charts initialized (9 containers created)
- ✅ setData() called successfully (no JS errors)

### Broken Component:
- ❌ uPlot rendering to canvas (nothing visible)

---

## 🚨 CRITICAL FILES TO CHECK

### Must Review:
1. `screener.js:96-155` - uPlot initialization config
2. `screener.js:167-188` - updateDeviationChart() function
3. Browser DevTools → Elements → find `<canvas>` under `#uplot-BTC_USDT`
4. Browser DevTools → Console → check for any uPlot warnings

### Likely Culprit:
- `card.clientWidth` = 0 during chart creation (charts tab not visible yet?)
- Y-axis auto-ranging disabled (fixed -0.5 to 0.5 too large?)
- Canvas element not attached to DOM properly

---

## 🎯 IMMEDIATE NEXT STEPS

1. **Add canvas size logging:**
   ```js
   const width = card.clientWidth;
   console.log(`[Canvas] ${symbol}: ${width}x150`);
   if (width === 0) console.error(`[Canvas] ${symbol}: ZERO WIDTH!`);
   ```

2. **Inspect DOM in browser:**
   - Open DevTools (F12) → Elements tab
   - Find: `<div id="chart-BTC_USDT">`
   - Check: Does `<canvas>` exist? What size?

3. **Test minimal uPlot:**
   ```js
   const test = new uPlot({
       width: 300, height: 150,
       scales: { x: {time:true}, y: {range:[0,1]} },
       series: [{}, {stroke:'red'}]
   }, [[Date.now()/1000], [0.5]], document.getElementById('grid'));
   ```

4. **If canvas 0x0:** Delay chart creation until Charts tab visible
5. **If canvas valid:** Adjust Y-axis range or investigate uPlot plugins

---

## 📱 USER'S LAST QUESTION

> "скажи ты точно понимаешь что это за график и из чего он должен строиться?"

**Answer:** YES, now fully understood:
- **Graph type:** Bid/Bid price deviation between exchanges
- **Should use:** `TickerData.BestBid` (orderbook best bid)
- **Currently uses:** `TradeData.Price` (last trade - WRONG!)
- **Main issue:** Charts empty despite data flow working
- **Likely cause:** Canvas rendering issue OR Y-axis range too large

---

## 🔧 RECOMMENDED APPROACH

1. **Debug rendering first** (canvas size, uPlot errors)
2. **Then fix data source** (BestBid vs TradeData.Price)
3. **Then optimize** (Y-axis range, visual improvements)

**Don't fix data source until rendering works!** Even with wrong data, charts should show SOMETHING.
