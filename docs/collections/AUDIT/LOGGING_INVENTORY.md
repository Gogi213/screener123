# LOGGING INVENTORY & ANALYSIS

**Date:** 2025-11-23  
**Project:** Collections (Trade Screener)  
**Scope:** Complete logging audit  

---

## Executive Summary

**Current State:** 🔴 **FRAGMENTED**

Multiple logging outputs with no centralization, inconsistent formats, and redundant file writers. Requires consolidation into structured logging pattern.

---

## Log File Inventory

### 1. Application Logs

#### `logs/app.log`
- **Source:** Microsoft.Extensions.Logging (ASP.NET Core)
- **Format:** Plain text
- **Rotation:** None
- **Size:** Growing unbounded
- **Level:** Information+
- **Content:** General application logs

**Example:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:5000
info: SpreadAggregator.Application.Services.RollingWindowService[0]
      [RollingWindow] Created new trade window: MEXC_BTCUSDT
```

**Issues:**
- ❌ No log rotation → Will fill disk
- ❌ Plain text → Hard to parse
- ❌ Mixed severity levels → Noisy

---

### 2. Trade Screener Logs

#### `logs/screener_trades.log`
- **Source:** `TradeScreenerService` (custom StreamWriter)
- **Format:** Plain text
- **Rotation:** None
- **Level:** WHALE trades only (>$10K)
- **Content:** High-value trades

**Example:**
```
[WHALE] 2025-11-23 01:58:19 | MEXC | ETHUSDT | Buy | Price: 2806.07 | Qty: 14.63551 | Value: $41,068.27
```

**Code:**
```csharp
// TradeScreenerService.cs:34
_whaleLogger = new StreamWriter("logs/screener_trades.log", append: true) 
{ 
    AutoFlush = true 
};
```

**Issues:**
- ❌ Duplicate output (also goes to console via ILogger)
- ❌ Hardcoded path
- ❌ No structured format
- ❌ Single-purpose file writer

---

### 3. WebSocket Logs

#### `logs/websocket.log`
- **Source:** `WebSocketLogger.cs` (?)
- **Status:** 🔴 **ORPHANED** - No references found

**Search Results:**
```bash
$ grep -r "WebSocketLogger" collections/src/
# No results (except the file itself)
```

**Recommendation:** DELETE file

---

### 4. BidBid Logs

#### `logs/bidbid_ICPUSDT_*.log`
- **Source:** `BidBidLogger.cs`
- **Format:** CSV
- **Target:** SINGLE SYMBOL (ICPUSDT)
- **Content:** Bid/Bid arbitrage spreads

**Example:**
```csv
Timestamp,Exchange1,Exchange2,Symbol,Bid1,Bid2,Spread
2025-11-23 01:30:00.123,Bybit,GateIo,ICPUSDT,12.34,12.30,0.325081
```

**Code:**
```csharp
// BidBidLogger.cs:49
if (symbol.Equals("ICPUSDT", StringComparison.OrdinalIgnoreCase))
{
    var fileName = $"bidbid_ICPUSDT_{timestamp}.log";
    // ...
}
```

**Issues:**
- ❌ Hardcoded single symbol (300+ symbols ignored)
- ❌ Narrow use case (arbitrage-specific)
- ❌ Custom logger instead of ILogger

**Recommendation:** DELETE or generalize

---

### 5. Performance Logs

#### `logs/performance/*.csv`
- **Source:** `PerformanceMonitor`
- **Format:** CSV
- **Content:** CPU, Memory, ThreadPool metrics
- **Status:** ✅ **ACTIVE**

**Example:**
```
Timestamp,CPU,Memory,ThreadPool,...
2025-11-23 01:30:00,5.2,123MB,2,4,0
```

**Recommendation:** KEEP (useful for diagnostics)

---

## Logging Code Analysis

### ILogger Usage

**Files using ILogger:**
```
SpreadAggregator.Presentation/Program.cs
SpreadAggregator.Application/Services/ExchangeHealthMonitor.cs
SpreadAggregator.Application/Services/DataCollectorService.cs
SpreadAggregator.Application/Services/TradeScreenerService.cs
SpreadAggregator.Application/Services/RollingWindowService.cs
SpreadAggregator.Infrastructure/Services/Charts/OpportunityFilterService.cs
SpreadAggregator.Infrastructure/Services/Charts/ParquetReaderService.cs
SpreadAggregator.Presentation/Controllers/TradeController.cs
SpreadAggregator.Presentation/Controllers/DashboardController.cs
SpreadAggregator.Presentation/Controllers/RealTimeController.cs
SpreadAggregator.Infrastructure/Services/BidBidLogger.cs
SpreadAggregator.Infrastructure/Services/BidAskLogger.cs
```

**Total:** 12 files use `ILogger<T>`

---

### Custom Loggers (Problematic)

#### 1. TradeScreenerService
```csharp
private readonly StreamWriter _whaleLogger;

public TradeScreenerService(...)
{
    _whaleLogger = new StreamWriter("logs/screener_trades.log", append: true)
    {
        AutoFlush = true
    };
}

private void LogWhaleTrade(TradeData trade)
{
    var logLine = $"[WHALE] {trade.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | ...";
    _whaleLogger.WriteLine(logLine);  // Custom file write
    _logger.LogInformation(logLine);  // ALSO logs to ILogger
}
```

**Problem:** Duplicate outputs, no structure

---

#### 2. BidBidLogger
```csharp
private readonly StreamWriter _icpWriter;
private readonly Channel<(...)> _logChannel;

public BidBidLogger(...)
{
    var fileName = $"bidbid_ICPUSDT_{timestamp}.log";
    _icpWriter = new StreamWriter(logPath, append: true);
}
```

**Problem:** Single-purpose, channel overhead for 1 symbol

---

## Configuration Analysis

### Current Logging Config

**Program.cs:**
```csharp
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("BingX", LogLevel.Warning);
builder.Logging.AddFilter("Bybit", LogLevel.Debug);
```

**Issues:**
- ❌ No file output configured
- ❌ No structured logging
- ❌ No log rotation
- ❌ Hardcoded filters

---

## Proposed Solution: Serilog Migration

### 1. Install Packages

```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Formatting.Json
```

### 2. Configuration

**appsettings.json:**
```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System.Net.Http": "Warning",
        "BingX": "Warning",
        "Bybit": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app.jsonl",
          "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/whale_trades.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "restrictedToMinimumLevel": "Information",
          "outputTemplate": "{Message:lj}{NewLine}",
          "levelSwitch": "$WhaleSwitch"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

### 3. Program.cs Modifications

```csharp
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/app.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .WriteTo.File(
        path: "logs/app.jsonl",
        formatter: new JsonFormatter(),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
);
```

### 4. Structured Logging Examples

**Before (TradeScreenerService):**
```csharp
var logLine = $"[WHALE] {timestamp} | {exchange} | {symbol} | {side} | ...";
_whaleLogger.WriteLine(logLine);
```

**After:**
```csharp
_logger.LogInformation(
    "[WHALE] {Timestamp} | {Exchange} | {Symbol} | {Side} | Price: {Price} | Qty: {Quantity} | Value: ${Value}",
    timestamp, exchange, symbol, side, price, quantity, value);
```

**JSON Output:**
```json
{
  "Timestamp": "2025-11-23T01:58:19.414Z",
  "Level": "Information",
  "MessageTemplate": "[WHALE] {Timestamp} | {Exchange} | {Symbol} | {Side} | Price: {Price} | Qty: {Quantity} | Value: ${Value}",
  "Properties": {
    "Exchange": "MEXC",
    "Symbol": "ETHUSDT",
    "Side": "Buy",
    "Price": 2806.07,
    "Quantity": 14.63551,
    "Value": 41068.27
  }
}
```

---

## Migration Plan

### Phase 1: Setup Serilog (30 min)
1. ✅ Install NuGet packages
2. ✅ Configure `appsettings.json`
3. ✅ Modify `Program.cs`
4. ✅ Test basic logging

### Phase 2: Remove Custom Loggers (1 hour)
1. ✅ Delete `TradeScreenerService._whaleLogger`
2. ✅ Delete `BidBidLogger` (or make generic)
3. ✅ Delete `WebSocketLogger.cs`
4. ✅ Delete `NullBidAskLogger`
5. ✅ Update all call sites to use `ILogger`

### Phase 3: Structured Logging (1 hour)
1. ✅ Convert log messages to structured format
2. ✅ Add semantic properties
3. ✅ Test JSON output

### Phase 4: Log Analysis Tools (Optional)
1. ✅ Add log viewer (Seq, Grafana Loki)
2. ✅ Add log queries
3. ✅ Add alerting

---

## Benefits

### Before
- ❌ 5 different log files
- ❌ 2 formats (plain text, CSV)
- ❌ No rotation
- ❌ No structure
- ❌ Duplicate outputs

### After
- ✅ 2 log files (text + JSON)
- ✅ Single format (structured)
- ✅ Daily rotation (30 days retention)
- ✅ Queryable JSON
- ✅ No duplication

---

## Conclusion

**Current Logging:** 🔴 Fragmented, inefficient  
**Proposed Solution:** 🟢 Centralized Serilog  
**Effort:** 2.5 hours  
**Risk:** Low (backward compatible)  

**Recommendation:** Proceed with Serilog migration in Phase 2 of refactor.
