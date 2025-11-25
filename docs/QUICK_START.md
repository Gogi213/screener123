# ⚡ Quick Start Guide - Continue Work

**Use this file to quickly resume work in a new chat session**

---

## 🎯 Current Objective

Implement **SPRINT-3**: Simple sorting by `trades/3m` + TOP-50 chart rendering

---

## 📁 Key Files

```
collections/src/SpreadAggregator.Application/Services/
└── TradeAggregatorService.cs          ← Server-side metrics (DONE)

collections/src/SpreadAggregator.Presentation/wwwroot/
├── js/screener.js                      ← Client logic (NEEDS UPDATE)
├── index.html                          ← UI (minimal changes)
└── styles.css                          ← Styling

docs/
├── SPRINT_CONTEXT.md                   ← Full context (read this!)
├── ARCHITECTURE.md                     ← Technical details
└── QUICK_START.md                      ← This file
```

---

## ✅ What's DONE

### SPRINT-1: Extended Metrics ✅
- Server calculates `trades/1m`, `trades/2m`, `trades/3m`
- WebSocket broadcasts metrics every 2 seconds
- All 2000 symbols monitored

### SPRINT-2: Advanced Benchmarks ✅
- `acceleration` - growth rate detection
- `hasPattern` - bot detection
- `imbalance` - buy/sell pressure
- **NOTE:** Implemented but NOT used for sorting yet

---

## 🔨 What's TODO (SPRINT-3)

### Server Changes (C#):

**File:** `TradeAggregatorService.cs`

**Line ~449:** Change sorting
```csharp
// BEFORE:
.OrderByDescending(m => m.Score)  // pump score

// AFTER:
.OrderByDescending(m => m.Trades3Min)  // trades/3m
```

**Line ~211-220:** Remove `top70_update` message (optional cleanup)

---

### Client Changes (JavaScript):

**File:** `screener.js`

#### 1. **Change sorting** (currently by `score`, need by `trades3m`):

**Find:** Line ~254-264
```javascript
allSymbols = msg.symbols
    .filter(s => !BLACKLIST.includes(s.symbol.toUpperCase()))
    .map(s => ({
        symbol: s.symbol,
        score: s.score,
        tradesPerMin: s.tradesPerMin,
        trades2m: s.trades2m,        // ← Already receiving
        trades3m: s.trades3m,        // ← Already receiving
        lastPrice: s.lastPrice
    }));
```

**Add sorting:**
```javascript
// After mapping, before renderPage()
allSymbols.sort((a, b) => b.trades3m - a.trades3m);  // ← ADD THIS
```

#### 2. **Limit to TOP-50:**

**Find:** `renderPage()` function (~line 55)
```javascript
function renderPage() {
    cleanupPage();
    
    // BEFORE: Render all
    allSymbols.forEach(s => createCard(s.symbol));
    
    // AFTER: Render only top 50
    const top50 = allSymbols.slice(0, 50);
    top50.forEach(s => createCard(s.symbol));
}
```

#### 3. **Change display `/1m` → `/3m`:**

**Find:** `updateCardStats()` function (~line 345)
```javascript
// BEFORE:
statsEl.textContent = `${count}/1m`;

// AFTER:
const trades3m = symbolActivity.get(symbol)?.trades3m || 0;
statsEl.textContent = `${trades3m}/3m`;
```

**NOTE:** You'll need to store `trades3m` in `symbolActivity` when receiving metrics!

#### 4. **Speed Sort toggle** (already exists, just verify):

**Find:** `reorderCardsWithoutDestroy()` (~line 405)
```javascript
function reorderCardsWithoutDestroy() {
    if (!smartSortEnabled) return;  // ← Respect toggle
    
    // Sort by trades3m instead of trades1m
    allSymbols.sort((a, b) => {
        const actA = symbolActivity.get(a.symbol)?.trades3m || 0;
        const actB = symbolActivity.get(b.symbol)?.trades3m || 0;
        return actB - actA;
    });
    
    renderPage();  // Will render top50
}
```

---

## 🧪 Testing Checklist

After changes, verify:

1. **Server:**
   - [ ] `dotnet build` succeeds
   - [ ] `dotnet run` works without errors
   - [ ] WebSocket messages logged correctly

2. **Client:**
   - [ ] Browser console shows no errors
   - [ ] WebSocket messages parsed correctly
   - [ ] Top-50 symbols rendered as charts
   - [ ] Cards show `/3m` instead of `/1m`
   - [ ] Speed Sort toggle works (freeze/unfreeze)

3. **Performance:**
   - [ ] CPU usage <5% (server)
   - [ ] Browser responsive with 50 charts
   - [ ] No memory leaks

---

## 🐛 Common Issues

### Issue: "screener.js corrupted after edit"
**Solution:** 
```bash
git checkout 59204ea -- src/SpreadAggregator.Presentation/wwwroot/js/screener.js
```

### Issue: "Charts flickering"
**Solution:** Already using incremental updates (`uplot.setData()`), should be fine

### Issue: "WebSocket disconnects"
**Solution:** Normal on page refresh, auto-reconnects in 3 seconds

---

## 📊 Expected Result

**Before SPRINT-3:**
- All 2000 symbols rendered as charts → BROWSER CRASH

**After SPRINT-3:**
- Only TOP-50 by `trades/3m` rendered
- Smooth performance (~50 charts)
- Display shows `285/3m` instead of `100/1m`
- Speed Sort button controls updates

---

## 🚀 Commands

```bash
# Build & Run
cd "c:\visual projects\screener123\collections"
dotnet build
dotnet run --project src\SpreadAggregator.Presentation

# Open browser
http://localhost:5000

# Check logs
# Look for: "[TradeAggregator] Metadata broadcast: 2358 symbols"

# Stop server
Ctrl+C

# Or kill all dotnet
taskkill /F /IM dotnet.exe /T
```

---

## 💬 Key Context for AI

**User wants:**
- Simple, not complex
- Fast performance
- TOP-50 charts (was 70, changed to 50, might be even less)
- Sort by `trades/3m` (NOT composite score or pump score)
- Speed Sort toggle for control

**User likes:**
- `acceleration` metric (show on card if > 2.0)
- `hasPattern` bot detection (show icon 🤖)
- `imbalance` buy/sell pressure (show 📈 or 📉)
- But these are for SPRINT-4 (future), not for sorting

**Technical notes:**
- All benchmark operations are CHEAP (<2% CPU)
- Server already sends all data via WebSocket
- Client just needs to filter/sort/render
- `uPlot` used for charts (incremental updates work well)

---

## 📖 Full Context

For detailed information, read:
1. `SPRINT_CONTEXT.md` - Complete session history
2. `ARCHITECTURE.md` - Technical architecture
3. Git commit `59204ea` - Last stable screener.js

---

**Ready to continue? Start with SPRINT-3 tasks above! 🚀**
