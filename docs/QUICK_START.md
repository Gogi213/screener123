# ⚡ Quick Start Guide - Continue Work

**Use this file to quickly resume work in a new chat session**

---

## 🎯 Current Objective

~~SPRINT-3: Simple sorting + TOP-30~~ ✅ **COMPLETE**

**Status:** System is production-ready! All core features implemented.

**Optional next steps:**
- Add imbalance indicator (📈/📉) visualization on cards
- Add hasPattern indicator (🤖) for bot detection
- Performance tuning if needed

---

## � Quick Commands

```bash
# Start backend (collections folder)
cd c:\visual projects\screener123\collections
dotnet build && dotnet run --project src\SpreadAggregator.Presentation

# Open frontend
http://localhost:5000/index.html
```

---

## ✅ What's DONE

### SPRINT-0: Infrastructure ✅
- OrchestrationService
- WebSocket server (port 8181)
- Rolling window (30 minutes)
- MEXC trade streaming

### SPRINT-1: Extended Metrics ✅
- Server calculates `trades/1m`, `trades/2m`, `trades/3m`
- WebSocket broadcasts metrics every 2 seconds
- All 2000+ symbols monitored

### SPRINT-2: Advanced Benchmarks ✅
- `acceleration` - growth rate detection (trades_current / trades_previous)
- `hasPattern` - bot detection (10+ same volume trades)
- `imbalance` - buy/sell pressure (0.0-1.0)
- **Calculated server-side** for TOP-500 symbols (performance optimization)

### SPRINT-3: Simple Sorting + TOP-30 + Performance ✅ **[COMPLETE 2025-11-25]**

**Server:**
- Sorts by `Trades3Min` (simplified from complex CompositeScore)
- Broadcasts all data via `all_symbols_scored` message every 2 seconds

**Client:**
- Renders only TOP-30 charts (reduced from 2000 for stability)
- Displays `X/3m` instead of `X/1m`
- Smart Sort uses server-provided `trades3m` data
- **Anti-flicker:** `renderPage()` only on first load + Smart Sort interval 10s
- **Performance:** Batch throttle 1000ms, scatter-only graphs (no lines/fill)

**Acceleration Indicator:**
- ✅ Always visible on all cards
- ⚫ Gray if < 2.0x (normal)
- � Orange if 2.0-3.0x (high)
- � Red if >= 3.0x (extreme)

**Freeze Button:**
- ✅ "🔥 Live Sort" - auto re-sorting every 10 seconds
- ✅ "❄️ Frozen" - freeze to study coins (no re-sorting)

**Bug Fixes:**
- ✅ Fixed sorting (was broken by local data overwriting server data)
- ✅ Removed `updateSymbolActivity()` - sorting now uses server-only data

---

## 📁 Project Structure

```
collections/
├── src/
│   ├── SpreadAggregator.Application/
│   │   └── Services/
│   │       └── TradeAggregatorService.cs  ← Core logic, metrics calculation
│   └── SpreadAggregator.Presentation/
│       └── wwwroot/
│           ├── index.html         ← UI entry point
│           ├── js/screener.js     ← Main JS logic
│           └── css/screener.css   ← Styles
```

---

## � Key Configuration

**Backend:**
- WebSocket: `http://0.0.0.0:8181`
- Broadcast interval: 2 seconds (`all_symbols_scored` message)
- Metrics calculated for TOP-500 symbols

**Frontend:**
- Blacklist: BTCUSDT, ETHUSDT, etc (see `screener.js` line 5)
- TOP-30 rendering limit
- Batch update: 1 second
- Smart Sort: 10 seconds (when enabled)

---

## 📊 Metrics Available

| Metric | Description | Where Calculated | Display |
|--------|-------------|------------------|---------|
| `trades3m` | Trades in last 3 min | Server | `285/3m` |
| `acceleration` | Growth rate (current/previous min) | Server (TOP-500) | `↑2.5x` |
| `imbalance` | Buy/sell pressure | Server (TOP-500) | Not displayed yet |
| `hasPattern` | Bot detection | Server (TOP-500) | Not displayed yet |

---

## � Known Issues

None! System is stable.

---

## 💡 Tips for Next Session

1. **If adding new features:** Start with server-side calculation in `TradeAggregatorService.cs`, then update client
2. **If charts flicker:** Check `isFirstLoad` flag and batch throttle in `screener.js`
3. **If sorting broken:** Ensure `symbolActivity` is updated ONLY from WebSocket, not local calculations

---

## � Development Notes

**Performance:**
- TOP-30 charts: ~100-150ms render
- Server CPU: ~2% for 2000 symbols
- WebSocket: Stable, no disconnects
- Memory: Controlled (circular buffer in chartData)

**Architecture decisions:**
- Simple sorting (trades/3m) instead of complex composite scores
- Server-side metrics calculation for accuracy
- Client displays server data, doesn't recalculate
- Scatter-only graphs for performance

---

See `SPRINT_CONTEXT.md` for detailed sprint history.
