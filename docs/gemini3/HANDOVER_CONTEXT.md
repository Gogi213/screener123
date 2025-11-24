# Handover Context: Mexc Screener Refactoring

**Date:** 2025-11-23  
**Status:** Phase 2 Sprint 1 Complete + Pipeline Optimized

## 🚀 Recent Changes (Last Session)
We performed a major overhaul of the Frontend UI/UX.

### 1. Modularization
The monolithic `screener.html` was split into:
- `wwwroot/screener.html`: Structure and imports only.
- `wwwroot/css/screener.css`: All styles (Premium Dark Theme).
- `wwwroot/js/screener.js`: Application logic (WebSocket, Charts, Formatting).

### 2. UI/UX Improvements
- **Premium Look:** Inter font, Zinc color palette, Glassmorphism header.
- **Chart Interaction:**
  - **Zoom:** Wheel/Pinch enabled.
  - **Pan:** Drag enabled (works best when zoomed in).
  - **Reset:** Double-click on chart.
- **Data Visualization:**
  - **Price Formatter:** `0.(5)123` format for low-cap coins.
  - **Dynamic Stats:** Trade count in the card header updates in real-time (reflects actual points on chart).
  - **History:** 30-minute rolling window (enforced by frontend filter).

### 3. Pipeline Optimization (2025-11-23)
Removed ALL components causing CPU/Memory overhead:
- ❌ **DataCollectorService** - No-op service (used NullDataWriter)
- ❌ **TradeScreenerService** - Whale trade file logging (I/O overhead)
- ❌ **Legacy WebSocket broadcast** - Port 8181 (unused by screener)
- ❌ **RawDataChannel writes** - Channel for disabled DataCollectorService
- ❌ **TradeScreenerChannel writes** - Channel for disabled TradeScreenerService

**Clean Pipeline:**
```
Mexc WebSocket → RollingWindowChannel → RollingWindowService → TradeController WebSocket → Frontend
```

**Performance Impact:** ~10-15% CPU reduction + File I/O eliminated

**See:** `docs/gemini3/OPTIMIZATION_REPORT_2025-11-23.md` for details.

### 4. Aggressive Cleanup - Round 2 (2025-11-24)
Removed ALL actively running background processes:
- ❌ **PerformanceMonitor** - File I/O every 1 second (DISK bottleneck!)
- ❌ **FleckWebSocketServer.Start()** - Legacy WebSocket actively listening on port 8181
- ❌ **ExchangeHealthMonitor** - Timer every 10 seconds (monitoring overhead)

**Result:**
- ✅ ZERO active timers
- ✅ ZERO File I/O operations  
- ✅ ZERO monitoring overhead
- ✅ Single WebSocket server (TradeController only)
- ✅ 2 services ONLY (OrchestrationServiceHost + RollingWindowServiceHost)

**Combined Impact (Round 1+2):** ~15-20% CPU reduction + All background noise eliminated

**See:** `docs/gemini3/AGGRESSIVE_CLEANUP_2025-11-24.md` for details.

## 🛠 Technical State
- **Backend:** `SpreadAggregator.Presentation` (ASP.NET Core).
- **Frontend:** Vanilla JS + Chart.js + chartjs-plugin-zoom.
- **Run Command:** `dotnet run --project src/SpreadAggregator.Presentation`
- **Access:** `http://localhost:5000/screener.html`

## 📋 Immediate Next Steps (Phase 2)
See `docs/gemini3/roadmap/phase-2-screener-refinement.md` for details.

1. **Client-Side Filtering:** Add search input for symbols.
2. **Real-Time Sorting:** Sort grid by trade activity dynamically.
3. **Volume Display:** Show 24h or 30m volume on cards.

## ⚠️ Known Issues / Notes
- The server might need a restart if WebSocket connections hang (rare).
- `TradeController.cs` streams *all* trades in the 30m window (no throttling), which is intentional for "Pro" mode.
