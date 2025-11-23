# Project Statistics - Collections

**Generated:** 2025-11-23  
**Project:** SpreadAggregator (Trade Screener)  

---

## Codebase Metrics

### Files
- **Total C# Files:** 67
- **Total Lines of Code:** 6,109
- **Average File Size:** 91 lines

### Project Structure
```
collections/
├── src/
│   ├── SpreadAggregator.Domain          # Entities, Models
│   ├── SpreadAggregator.Application     # Business Logic
│   ├── SpreadAggregator.Infrastructure  # External Services
│   └── SpreadAggregator.Presentation    # API, Controllers
├── tests/
│   └── SpreadAggregator.Tests
├── docs/
├── logs/
└── tools/
```

---

## Component Breakdown

### Active Components (Keep)

#### Core Services
- `OrchestrationService` - Exchange coordination
- `RollingWindowService` - In-memory cache
- `TradeScreenerService` - Whale trade detection
- `TradeController` - WebSocket API (NEW)

#### Exchange Clients
- Mexc (active)
- Binance (active)
- ~5 disabled exchanges~

#### Frontend
- `screener.html` - Modern UI
- `screener.js` - Chart.js visualization
- `screener.css` - Dark theme

---

### Legacy Components (Remove)

#### Controllers
- `DashboardController` - Uses non-existent Parquet files (200 LoC)
- `RealTimeController` - Spread-based, deprecated (200 LoC)

#### Services
- `ParquetReaderService` - No data source (200 LoC)
- `BidBidLogger` - Single symbol hardcoded (150 LoC)
- `BidAskLogger` - Null implementation (50 LoC)
- `WebSocketLogger` - Orphaned (100 LoC)

**Total Removable:** ~900 lines (15% of codebase)

---

## Logging Breakdown

### Log Files
```
logs/
├── app.log                    # 📝 Main logs (growing unbounded)
├── screener_trades.log        # 🐋 Whale trades (custom logger)
├── websocket.log              # ❌ Orphaned
├── bidbid_ICPUSDT_*.log       # 📊 Single symbol CSV
└── performance/               # ✅ Metrics (CSV)
```

### Log Sources
- **ILogger:** 12 files
- **Custom StreamWriter:** 2 files (screener, bidbid)
- **Orphaned:** 1 file (websocket)

---

## Dependencies

### NuGet Packages
- ASP.NET Core 9.0
- Microsoft.Data.Analysis (Parquet) ← UNUSED
- Channel primitives
- Exchange clients (CCXT-based)

### External Dependencies
- **Mock data:** `/analyzer/summary_stats/*.csv`
- **Parquet files:** ❌ NOT GENERATED
- **QuestDB:** 🔜 PROPOSED

---

## Performance Characteristics

### Current (In-Memory)
- **RAM Usage:** ~500MB (300 symbols × 30 minutes)
- **Write Latency:** <1µs
- **Read Latency:** <1ms
- **Throughput:** 5K trades/sec

### With QuestDB (Proposed)
- **RAM Usage:** ~50MB (-90%)
- **Write Latency:** <1µs (batched)
- **Read Latency:** 5-20ms (warm data)
- **Throughput:** 4M writes/sec

---

## Technical Debt

### Debt Score: ⚠️ MODERATE

| Category | Debt Level | LoC | Priority |
|----------|------------|-----|----------|
| Dead Code | High | 900 | 🔴 Remove |
| Logging Duplication | Medium | 200 | 🟡 Refactor |
| Config Inconsistency | Low | 50 | 🟢 Clean |
| Architecture Split | Medium | - | 🟡 Design |

---

## Refactoring Impact

### Before
- **Files:** 67
- **LoC:** 6,109
- **Dead Code:** 15%
- **RAM:** 500MB
- **Log Outputs:** 5

### After (Target)
- **Files:** 52 (-22%)
- **LoC:** 4,200 (-31%)
- **Dead Code:** 0%
- **RAM:** 50MB (-90% with QuestDB)
- **Log Outputs:** 2 (text + JSON)

---

## Historical Context

### Evolution
1. **v0.1:** Arbitrage HFT (spread calculation)
2. **v0.5:** Added Trade Screener (whale detection)
3. **v1.0:** Primary focus → Trade Screener
4. **Current:** Hybrid (legacy + new)
5. **v2.0:** Pure Trade Screener (proposed)

### Unused Features
- ❌ Parquet file reader/writer
- ❌ Arbitrage dashboard
- ❌ Multi-exchange spread analysis
- ❌ BidBid logging (except ICPUSDT)

---

## Resource Usage

### Disk
- **Code:** ~100KB
- **Dependencies:** ~200MB (NuGet)
- **Logs:** Growing (no rotation)
- **Data:** None (in-memory only)

### Memory
- **Application:** 500MB (trade windows)
- **Exchange Clients:** 50MB
- **ASP.NET Core:** 100MB
- **Total:** ~650MB

### Network
- **WebSocket Connections:** 2 (Mexc, Binance)
- **Outbound:** ~100KB/sec (exchange data)
- **Inbound:** ~10KB/sec per client (dashboard)

---

## Conclusion

**Codebase Health:** ⚠️ **MODERATE**

- ✅ Core functionality solid
- ⚠️ 15% dead code
- ⚠️ Fragmented logging
- ✅ Good performance

**Recommended Action:** Proceed with refactoring audit recommendations.

---

**See Also:**
- [Refactoring Audit](./AUDIT/REFACTORING_AUDIT_2025-11-23.md)
- [Logging Inventory](./AUDIT/LOGGING_INVENTORY.md)
- [QuestDB Proposal](./PROPOSALS/QUESTDB_INTEGRATION.md)
