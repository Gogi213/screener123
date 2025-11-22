# Handover Context: Mexc Screener Refactoring

**Date:** 2025-11-22
**Status:** Phase 2 Started (Sprint 1 Complete)

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
