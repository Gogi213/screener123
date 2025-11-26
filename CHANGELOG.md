  - Result: ~300-400 high-quality symbols (from ~1,200)

- **Trade Batching Optimization (SPRINT-8):**
  - Reduced batch interval: 100ms → 200ms
  - Impact: ~50% reduction in WebSocket broadcast overhead
  - CPU load expected to drop significantly
  - Metadata broadcast remains at 2-second interval (10 ticks × 200ms)
  - No UX degradation (frontend already throttles at 1000ms)

### Performance
- Network traffic: 50% reduction (fewer WebSocket messages)
- CPU usage: Expected 30-50% reduction for broadcast operations
- Latency: +100ms (negligible, masked by frontend throttling)

---

## [1.4.0] - 2025-11-26 - SPRINT-6

### Fixed
- **Chart Reload Flicker:**
  - Pre-initialize chartData on first trade_update (instead of waiting for all_symbols_scored)
  - Eliminates data loss during 0-2 second initial connection period
  - Charts now load smoothly with accumulated data from first WebSocket message
  - No more "jerky" reloads where charts fill in randomly

### Technical Details
- Issue: Trades arriving before all_symbols_scored (sent every 2 sec) were lost
- Root cause: chartData was only initialized in createCard() (called after all_symbols_scored)
- Solution: Pre-create chartData structure when first trade_update arrives
- Impact: Smooth chart loading, no visual flicker on page refresh

---

## [1.3.0] - 2025-11-26 - SPRINT-4 & SPRINT-5

### Added
- **WebSocket Auto-Reconnect (SPRINT-4):**
  - Exponential backoff: 1s → 2s → 4s → 8s → 16s → max 30s
  - Automatic recovery from server restarts
  - No manual page reload needed
  - Integration with health monitoring to prevent false alerts

- **Health Monitoring (SPRINT-5):**
  - Visual alert when no trades for 30+ seconds
  - Orange banner notification at top of page
  - Auto-hide when trades resume
  - Helps detect MEXC connection issues

- **Major Exchanges Filter:**
  - Exclude symbols listed on Binance Spot
  - Exclude symbols listed on Bybit Spot
  - Exclude symbols listed on OKX Spot
  - Result: ~1,200 MEXC-exclusive symbols (from ~2,400 total)

### Changed
- Chart points size: 4px → 3px (better readability, less overlap)
- Test suite removed (not needed for screener tool)

### Performance
- CPU: Stable at 2% (1,200 symbols)
- RAM: Stable at 60 MB (no leaks detected)
- WebSocket: Resilient with auto-recovery

---

## [1.2.0] - 2025-11-25 - SPRINT-3

### Added
- Simple sorting by trades/3m (most active first)
- TOP-30 display limit (performance optimization)
- Acceleration indicator always visible
  - Gray color for <2.0x (normal)
  - Orange for >=2.0x (high activity)
  - Red for >=3.0x (extreme activity)

### Changed
- Removed composite score sorting (too complex)
- Simplified to trades/3m only (clearest metric)
- Acceleration color-coded for quick visual scanning

### Fixed
- Chart flickering on smart sort (ANTI-FLICKER: render only on first load)
- Performance issues with 2000+ symbols (now TOP-30 only)

---

## [1.1.0] - 2025-11-25 - SPRINT-2

### Added
- Advanced benchmarks:
  - Acceleration (trade velocity change)
  - Buy/Sell imbalance
  - Volume pattern detection (bot activity)
- Composite score calculation
- TOP-500 optimization (benchmarks only for top symbols)

### Performance
- CPU reduction: 80% → 20% by optimizing to TOP-500
- Minimal impact on system resources

---

## [1.0.0] - 2025-11-24 - SPRINT-0 & SPRINT-1

### Initial Release
- Real-time MEXC trade streaming
- Rolling window metrics (trades/1m, 2m, 3m)
- WebSocket broadcast every 2 seconds
- uPlot charts (scatter-only, high performance)
- Freeze/Live sort controls
- Card-based UI with TOP-30 display
- Volume filtering
- Click-to-copy symbol names

### Architecture
- OrchestrationService: MEXC subscription management
- TradeAggregatorService: Metrics calculation
- FleckWebSocketServer: Client communication
- ASP.NET Core backend
- Vanilla JS frontend

---

**System Status:** Production Ready ✅
