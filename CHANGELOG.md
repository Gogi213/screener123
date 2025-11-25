# CHANGELOG

## [3.0.0] - 2025-11-25 - SPRINT-3 Complete

### Added
- **Acceleration Indicator:** Always visible on cards, color-coded (gray < 2x, orange 2-3x, red 3x+)
- **Freeze Button:** Renamed "Speed Sort" → "Live Sort/Frozen" for clarity
- **Performance:** Scatter-only graphs (removed stroke/fill for 2-3x faster rendering)
- **Performance:** Batch throttle increased to 1 second (reduced CPU load)

### Fixed
- **Critical:** Sorting broken - client was overwriting server data with local calculations
- **Critical:** Removed `updateSymbolActivity()` - sorting now uses server-only data
- **Critical:** Chart flickering - `renderPage()` now only called on first load + Smart Sort

### Changed
- TOP-30 display (reduced from TOP-50 for stability)
- Smart Sort interval: 2s → 10s (less aggressive)
- Status text: "sorted by trades/3m"
- Card display: `/1m` → `/3m`

---

## [2.0.0] - 2025-11-24 - SPRINT-2 Complete

### Added
- Advanced benchmarks:
  - `acceleration` - growth rate (trades current / trades previous)
  - `hasPattern` - bot detection (repeated volumes)
  - `imbalance` - buy/sell pressure (0.0-1.0)
- Composite score calculation
- Benchmark optimization: calculate only for TOP-500

---

## [1.0.0] - 2025-11-23 - SPRINT-1 Complete

### Added
- Extended metrics: trades/1m, 2m, 3m
- WebSocket broadcast every 2 seconds
- All 2000+ symbols monitored

---

## [0.1.0] - 2025-11-22 - SPRINT-0 Infrastructure

### Added
- OrchestrationService for MEXC subscription
- TradeAggregatorService with 30-min rolling window
- WebSocket server (Fleck) on port 8181
- Basic UI with uPlot charts
- Card grid layout
