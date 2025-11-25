# ⚡ Quick Start Guide - Continue Work

**Use this file to quickly resume work in a new chat session**

---

## 🎯 Current Objective

~~SPRINT-3: Simple sorting + TOP-30~~ ✅ **COMPLETE**

**Next:** Implement **SPRINT-4** - Benchmark Indicators on chart cards (optional UI polish)

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
- **NOTE:** Implemented but NOT used for sorting (SPRINT-3 uses simple trades/3m)

### SPRINT-3: Simple Sorting + TOP-30 + Anti-Flicker ✅ **[NEW]**
- **Server:** Sorts by `Trades3Min` (simplified from CompositeScore)
- **Client:** Renders only TOP-30 charts (reduced from 50 for stability)
- **Client:** Displays `X/3m` instead of `X/1m`
- **Client:** Smart Sort uses `trades3m` with 10-second interval
- **CRITICAL FIX:** Anti-flicker optimization
  - `renderPage()` только при первой загрузке (isFirstLoad flag)
  - Smart Sort interval: 2s → 10s
  - **Result:** 0 мерцания при выключенной Smart Sort

---

---

## 🔨 What's NEXT (SPRINT-4 - Optional)

### Goal: Visual Benchmark Indicators

Add visual indicators to chart cards showing the advanced benchmarks from SPRINT-2:

**Indicators:**
- 🔥 **Acceleration** - if `acceleration > 2.0` display `🔥${acceleration}x`
- 🤖 **Bot Pattern** - if `hasPattern = true` display bot icon
- 📈 **Buy Pressure** - if `imbalance > 0.7` show upward trend
- 📉 **Sell Pressure** - if `imbalance < -0.7` show downward trend

**Card mockup:**
```
┌────────────────────────────┐
│ BTCUSDT              45000 │
│ 285/3m  🔥2.5x  🤖  📈    │
│ ══════ Chart ══════       │
└────────────────────────────┘
```

**Files to modify:**
- `screener.js` - add indicator rendering logic in `createCard()`
- `screener.css` - styling for indicators

**ETA:** 2-3 hours (optional polish)

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
