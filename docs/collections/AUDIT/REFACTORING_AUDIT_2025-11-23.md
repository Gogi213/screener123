# REFACTORING AUDIT: Collections Project

**Date:** 2025-11-23  
**Auditor:** Sequential Thinking Analysis  
**Scope:** Complete codebase review (collections project)  
**Status:** In Progress  

---

## Executive Summary

**Overall Assessment:** ⚠️ **MODERATE TECHNICAL DEBT**

The project is functional but contains significant legacy code, architectural inconsistencies, and redundant components from the transition from **Arbitrage HFT** → **Trade Screener**. Estimated **30-40% code reduction** possible through refactoring.

**Critical Findings:**
1. 🔴 **Dual-purpose architecture** - Serving both legacy arbitrage + new screener
2. 🟡 **Unused services** - BidBid/BidAsk loggers, Parquet readers
3. 🟡 **Inconsistent logging** - Multiple log files, no centralization
4. 🟢 **Core functionality** - RollingWindow, Orchestration work well

---

## Methodology

### Sequential Thinking Process

**Thought 1: Project Purpose**
- Current: **Trade Screener** (high-volume trade monitoring)
- Legacy: **Arbitrage HFT** (spread calculation between exchanges)
- Status: **Transition incomplete** - old code still present

**Thought 2: Data Flow Analysis**
```
Exchange APIs → OrchestrationService → Channel → Consumers
                                                    ├─ DataCollectorService (DEPRECATED?)
                                                    ├─ RollingWindowService (ACTIVE)
                                                    └─ TradeScreenerService (ACTIVE)
```

**Thought 3: Entry Points**
- `/` → screener.html (NEW)
- `/api/dashboard_data` → LEGACY (Parquet-based)
- `/ws/realtime_charts` → LEGACY (Spread-based)
- `/ws/trades/{symbol}` → NEW (Trade-based)
- `/api/trades/symbols` → NEW

**Thought 4: Dependencies**
- `analyzer` project CSV files → MOCK data in production
- Parquet files → NOT GENERATED
- BidBid logs → SINGLE SYMBOL (ICPUSDT)

---

## Detailed Findings

### 1. LEGACY CODE (High Priority)

#### A. Dashboard Controller (UNUSED)

**File:** `DashboardController.cs`  
**Status:** 🔴 **REMOVE**  
**Reason:** Depends on Parquet files that don't exist

```csharp
// DashboardController.cs
[HttpGet("dashboard_data")]
public async IAsyncEnumerable<ChartDataDto> GetDashboardData()
{
    var opportunities = _opportunityFilter.GetFilteredOpportunities(); // READS MOCK CSV
    
    foreach (var opp in opportunities)
    {
        var chartData = await _parquetReader.LoadAndProcessPairAsync(
            opp.Symbol, opp.Exchange1, opp.Exchange2); // PARQUET FILES DON'T EXIST
        // ...
    }
}
```

**Issues:**
- References `ParquetReaderService` (unused)
- Hardcoded to `index.html` (legacy dashboard)
- No data source in production
- Mock CSV file created manually

**Recommendation:** DELETE entire controller

---

#### B. RealTimeController (LEGACY)

**File:** `RealTimeController.cs`  
**Status:** 🟡 **DEPRECATE**  
**Reason:** Built for arbitrage spread monitoring, not trade screening

```csharp
// RealTimeController.cs - Spread-based (OLD)
[HttpGet("realtime_charts")]
public async Task HandleWebSocket()
{
    // Uses OpportunityFilterService → reads analyzer CSV
    var opportunities = _opportunityFilter.GetFilteredOpportunities();
    
    // Subscribes to SPREAD windows (Bid1 vs Bid2)
    var chartData = _rollingWindow.JoinRealtimeWindows(
        opp.Symbol, opp.Exchange1, opp.Exchange2); // SPREAD CALCULATION
}
```

**vs NEW:**

```csharp
// TradeController.cs - Trade-based (NEW)
[HttpGet("/ws/trades/{symbol}")]
public async Task StreamTrades(string symbol)
{
    // Direct trade streaming (no spread calculation)
    var trades = _rollingWindow.GetTrades(symbol);
}
```

**Recommendation:** Mark `@Obsolete`, migrate to TradeController

---

#### C. Opportunity Filter Service (MOCK DATA)

**File:** `OpportunityFilterService.cs`  
**Status:** 🟡 **REFACTOR**  

```csharp
public List<Opportunity> GetFilteredOpportunities()
{
    var csvFiles = Directory.GetFiles(_analyzerStatsPath, "*.csv");
    // Reads: /analyzer/summary_stats/mock_stats.csv
    
    // PROBLEM: Hardcoded mock data!
    // BTC/USDT,Mexc,Binance,10  <-- Not real analysis
}
```

**Recommendation:** 
- Option 1: **Delete** (not needed for Trade Screener)
- Option 2: Generate from RollingWindow data

---

### 2. LOGGING MESS (Medium Priority)

#### Current State

**Log Files Found:**
```
logs/
├── app.log                    # ??? (General?)
├── screener_trades.log        # TradeScreenerService whale trades
├── websocket.log              # WebSocketLogger (UNUSED?)
├── bidbid_ICPUSDT_*.log       # BidBidLogger (SINGLE SYMBOL!)
└── performance/*.csv          # PerformanceMonitor
```

**Log Outputs:**
1. **Console** (stdout)
2. **app.log** (file)
3. **screener_trades.log** (TradeScreenerService only)
4. **bidbid logs** (BidBidLogger only)
5. **websocket.log** (WebSocketLogger - UNUSED?)

**Issues:**
- ❌ No centralized logging strategy
- ❌ Duplicate outputs (console + file)
- ❌ No structured logging (JSON)
- ❌ No log rotation
- ❌ No log levels per service

#### Logging Components

**A. TradeScreenerService**
```csharp
// Custom file logger
var logPath = "logs/screener_trades.log";
_whaleLogger = new StreamWriter(logPath, append: true) { AutoFlush = true };

// PROBLEM: Hardcoded path, no rotation, duplicates ILogger output
```

**B. BidBidLogger**
```csharp
// Logs ONLY "ICPUSDT" bidbid spreads
if (symbol.Equals("ICPUSDT", StringComparison.OrdinalIgnoreCase))
{
    var icpBidBidFileName = $"bidbid_ICPUSDT_{timestamp}.log";
    // PROBLEM: Single-purpose service for ONE symbol
}
```

**C. WebSocketLogger**
```csharp
// File: WebSocketLogger.cs
// PROBLEM: No references found in codebase - ORPHANED?
```

**Recommendations:**

1. **Centralized Logging:**
```csharp
// Use Serilog with sinks
builder.Services.AddSerilog((services, lc) => lc
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/app.log", 
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/trades.jsonl", 
        formatter: new JsonFormatter(),
        rollingInterval: RollingInterval.Day)
);
```

2. **Remove Custom Loggers:**
- DELETE `TradeScreenerService._whaleLogger`
- DELETE `BidBidLogger` (or make generic)
- DELETE `WebSocketLogger` (orphaned)

3. **Structured Logging:**
```csharp
_logger.LogInformation(
    "[WHALE] {Exchange} {Symbol} {Side} Price: {Price} Qty: {Quantity} Value: ${Value}",
    "MEXC", "BTCUSDT", "Buy", 42000, 1.5, 63000);
```

---

### 3. DEAD CODE (Low Priority)

#### A. DataCollectorService

**File:** `DataCollectorService.cs`  
**Status:** 🔴 **REFACTOR/DELETE**

```csharp
public class DataCollectorService : BackgroundService
{
    // Reads from RawDataChannel
    // Writes to DataWriter
    
    // PROBLEM: Recording.Enabled = false in config!
}
```

**Analysis:**
- `Recording.Enabled = false` → Service does nothing
- `NullDataWriter` registered → No actual writing
- Still runs in background → Wastes CPU cycles

**Recommendation:** 
- Make conditional: `if (config["Recording:Enabled"] == "true")`
- Or delete entirely

---

#### B. BidAsk/BidBid Loggers

**Files:** `BidAskLogger.cs`, `BidBidLogger.cs`  
**Status:** 🟡 **REMOVE OR GENERALIZE**

**BidAskLogger:**
```csharp
public class NullBidAskLogger : IBidAskLogger
{
    public Task LogAsync(SpreadData spreadData, DateTime localTimestamp) 
        => Task.CompletedTask;
}
```
- Null implementation registered
- Never used
- Interface kept for backward compat

**BidBidLogger:**
```csharp
if (symbol.Equals("ICPUSDT", StringComparison.OrdinalIgnoreCase))
{
    // Log ONLY this one symbol
}
```
- Single-purpose
- Hardcoded symbol
- Consumes resources for 1 out of 300+ symbols

**Recommendation:** DELETE both, use centralized logging

---

#### C. Parquet Reader Service

**File:** `ParquetReaderService.cs`  
**Status:** 🔴 **DELETE**  
**Reason:** No Parquet files exist in production

```csharp
public async Task<ChartData?> LoadAndProcessPairAsync(...)
{
    var filePath1 = Path.Combine(_dataRootPath, exchange1, fileName);
    // File doesn't exist → returns null
}
```

**Used By:** `DashboardController` (also unused)

**Recommendation:** DELETE entire service

---

### 4. ARCHITECTURAL ISSUES

#### A. Dual-Purpose RollingWindowService

**Current:** Handles BOTH trades AND spreads

```csharp
private void ProcessData(MarketData data)
{
    if (data is SpreadData spreadData)
    {
        // LEGACY: Process spread (arbitrage)
        ProcessLastTickMatching(spreadData);
    }
    else if (data is TradeData tradeData)
    {
        // NEW: Process trade (screener)
        AddTradeToWindow(tradeData);
    }
}
```

**Issue:** Mixed concerns

**Recommendation:** Split into:
- `SpreadWindowService` (for arbitrage)
- `TradeWindowService` (for screener)

---

#### B. Configuration Inconsistency

**appsettings.json:**
```json
{
  "Recording": {
    "Enabled": false  // But service still runs!
  },
  "Analyzer": {
    "StatsPath": ".../analyzer/summary_stats"  // Uses MOCK data
  },
  "ExchangeSettings": {
    "_comment_Binance": { ... },  // Prefix to disable
    "Mexc": { ... }
  }
}
```

**Issues:**
- Comment-based disabling (fragile)
- Mock data paths
- Unused settings

**Recommendation:** Clean configuration schema

---

### 5. EXCHANGE CLIENT CODE

**Issue:** Disabled exchanges via `_comment_` prefix

```json
"_comment_BingX": { ... },
"_comment_Bitget": { ... },
// Logs: [ERROR] No client found for exchange: _comment_BingX
```

**Recommendation:** Remove commented entries

---

## Summary Statistics

### Code Metrics

| Category | Count | Status |
|----------|-------|--------|
| **Total Files** | ~50 | - |
| **Legacy Code** | ~15 files | 🔴 Remove |
| **Active Code** | ~25 files | 🟢 Keep |
| **Mixed Purpose** | ~10 files | 🟡 Refactor |

### Deletion Candidates

| Component | LoC | Impact | Priority |
|-----------|-----|--------|----------|
| DashboardController | 100 | None | HIGH |
| ParquetReaderService | 200 | None | HIGH |
| BidAskLogger | 50 | None | MEDIUM |
| BidBidLogger | 150 | Low | MEDIUM |
| WebSocketLogger | 100 | None | LOW |
| RealTimeController | 200 | Medium | LOW |

**Total Deletable:** ~800 lines

---

## Refactoring Roadmap

### Phase 1: Cleanup (2 hours)
1. ✅ Delete `DashboardController`
2. ✅ Delete `ParquetReaderService`
3. ✅ Delete `WebSocketLogger`
4. ✅ Remove `_comment_*` exchanges from config
5. ✅ Replace `NullBidAskLogger` with removal

### Phase 2: Logging (3 hours)
1. ✅ Implement Serilog with structured logging
2. ✅ Remove custom file loggers
3. ✅ Centralize to `logs/app.jsonl`
4. ✅ Add log rotation
5. ✅ Delete BidBidLogger

### Phase 3: Architecture (4 hours)
1. ✅ Split `RollingWindowService` → Trade + Spread
2. ✅ Mark `RealTimeController` as `[Obsolete]`
3. ✅ Clean configuration schema
4. ✅ Document data flow

### Phase 4: Optional (QuestDB)
1. ✅ Implement QuestDB integration (see QUESTDB_INTEGRATION.md)

---

## Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Breaking production | High | Low | Phased rollout, feature flags |
| Data loss | Medium | Low | Test thoroughly before deploy |
| Performance regression | Low | Low | Benchmark before/after |

---

## Next Steps

1. **Review Findings** - Approve deletion list
2. **Create Branch** - `refactor/cleanup-legacy-code`
3. **Execute Phase 1** - Low-risk deletions
4. **Test** - Verify screener still works
5. **Deploy** - Monitor for 24h
6. **Continue** - Phase 2, 3, 4

---

## Appendix: File Inventory

### Active Code (Keep)
- ✅ `OrchestrationService.cs`
- ✅ `RollingWindowService.cs`
- ✅ `TradeScreenerService.cs`
- ✅ `TradeController.cs`
- ✅ `ExchangeClients/*`
- ✅ `screener.html`, `screener.js`

### Legacy Code (Delete)
- 🔴 `DashboardController.cs`
- 🔴 `ParquetReaderService.cs`
- 🔴 `WebSocketLogger.cs`
- 🔴 `NullBidAskLogger.cs`
- 🔴 `index.html` (old dashboard)

### Mixed (Refactor)
- 🟡 `RealTimeController.cs` - Deprecate
- 🟡 `BidBidLogger.cs` - Genericize or delete
- 🟡 `OpportunityFilterService.cs` - Remove mock data
- 🟡 `DataCollectorService.cs` - Make conditional

---

**Status:** Awaiting approval to proceed with Phase 1
