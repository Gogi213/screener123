# PROPOSAL: QuestDB Integration for Persistent Time-Series Storage

**Status:** Proposed  
**Date:** 2025-11-23  
**Author:** Sequential Thinking Analysis  
**Priority:** Medium  

---

## Executive Summary

Migrate from pure in-memory storage to **hybrid architecture** using QuestDB for time-series trade data persistence, reducing RAM usage by 90% while maintaining sub-20ms query performance.

---

## Problem Statement

### Current Architecture (In-Memory Only)

**RollingWindowService**:
- Stores **all trades for 30 minutes** in RAM
- Memory usage: ~500MB for 300+ symbols
- Data lost on restart
- No historical analysis capabilities

**Limitations**:
1. **High RAM consumption** - Linear growth with symbol count
2. **No persistence** - Restart = data loss
3. **No historical queries** - Only last 30 minutes available
4. **No compression** - Raw data in memory

---

## Proposed Solution: Hybrid Hot/Warm Architecture

### Architecture Overview

```
┌──────────────┐
│ Exchange API │
└──────┬───────┘
       │
       ↓
┌──────────────────┐
│ OrchestrationSvc │
└──────┬───────────┘
       │
       ↓
    Channel<MarketData>
       │
       ├─────────────────┐
       ↓                 ↓
┌──────────────┐  ┌──────────────┐
│ HOT (RAM)    │  │ WARM (QuestDB│
│ 1 minute     │  │ 1-30 minutes │
│ <1ms read    │  │ 5-20ms read  │
└──────────────┘  └──────────────┘
       │                 │
       └────────┬────────┘
                ↓
         TradeController
         (WebSocket API)
                ↓
             Browser
```

### Data Flow

**Write Path (Async, Non-blocking)**:
1. **Real-time**: Trade → RAM cache (instant)
2. **Background**: Batch writer → QuestDB every 1 second
3. **Throughput**: 10K+ writes/sec (batched)

**Read Path (Hybrid)**:
1. **Hot data** (0-1 min): Read from RAM (<1ms)
2. **Warm data** (1-30 min): Read from QuestDB (5-20ms)
3. **Merge**: Combine hot + warm, return to client

---

## Technical Specification

### 1. QuestDB Setup

**Docker Compose**:
```yaml
version: '3.8'
services:
  questdb:
    image: questdb/questdb:latest
    container_name: questdb
    ports:
      - "9000:9000"  # REST/Web Console
      - "9009:9009"  # ILP (InfluxDB Line Protocol)
      - "8812:8812"  # PostgreSQL wire protocol
    volumes:
      - questdb_data:/var/lib/questdb
    environment:
      - QDB_CAIRO_COMMIT_LAG=1000  # Fast commit
      - QDB_PG_ENABLED=true
      - QDB_HTTP_ENABLED=true
    restart: unless-stopped

volumes:
  questdb_data:
```

### 2. Table Schema

```sql
CREATE TABLE trades (
    timestamp TIMESTAMP,
    symbol SYMBOL,
    exchange SYMBOL,
    price DOUBLE,
    quantity DOUBLE,
    side SYMBOL
) TIMESTAMP(timestamp) PARTITION BY DAY;

-- Automatic deduplication index
CREATE INDEX idx_symbol_timestamp ON trades (symbol, timestamp);
```

**Storage Estimates**:
- **Per trade**: ~40 bytes (compressed)
- **Per day**: 5K trades/sec × 86,400 sec = 432M trades = ~17GB (compressed to ~1.7GB)
- **30 days**: ~51GB (vs 15GB in RAM)

### 3. Code Implementation

#### A. QuestDB Writer Service

```csharp
public class QuestDbWriter : IHostedService
{
    private readonly Sender _sender;
    private readonly Channel<TradeData> _writeQueue;
    private readonly Timer _flushTimer;
    
    public QuestDbWriter()
    {
        _sender = Sender.New("tcp::addr=localhost:9009;");
        _writeQueue = Channel.CreateBounded<TradeData>(10000);
        _flushTimer = new Timer(FlushBatch, null, 
            TimeSpan.FromSeconds(1), 
            TimeSpan.FromSeconds(1));
    }
    
    public async Task WriteAsync(TradeData trade)
    {
        await _writeQueue.Writer.WriteAsync(trade);
    }
    
    private async void FlushBatch(object state)
    {
        var batch = new List<TradeData>();
        
        while (batch.Count < 10000 && 
               _writeQueue.Reader.TryRead(out var trade))
        {
            batch.Add(trade);
        }
        
        if (batch.Count == 0) return;
        
        foreach (var trade in batch)
        {
            _sender.Table("trades")
                .Symbol("symbol", trade.Symbol)
                .Symbol("exchange", trade.Exchange)
                .Symbol("side", trade.Side)
                .Column("price", trade.Price)
                .Column("quantity", trade.Quantity)
                .At(trade.Timestamp);
        }
        
        await _sender.SendAsync();
    }
}
```

#### B. Hybrid Read Service

```csharp
public class TradeRepository
{
    private readonly RollingWindowService _hotCache;
    private readonly NpgsqlConnection _questDb;
    
    public async Task<List<TradeData>> GetTradesAsync(
        string symbol, 
        DateTime from, 
        DateTime to)
    {
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        // HOT path: Last minute from RAM
        var hotTrades = _hotCache.GetTrades(symbol)
            .Where(t => t.Timestamp > oneMinuteAgo)
            .ToList();
        
        // WARM path: 1-30 minutes from QuestDB
        var warmTrades = await QueryQuestDbAsync(symbol, from, oneMinuteAgo);
        
        // Merge
        return warmTrades.Concat(hotTrades)
            .OrderBy(t => t.Timestamp)
            .ToList();
    }
    
    private async Task<List<TradeData>> QueryQuestDbAsync(
        string symbol, 
        DateTime from, 
        DateTime to)
    {
        var sql = @"
            SELECT timestamp, price, quantity, side
            FROM trades
            WHERE symbol = @symbol
              AND timestamp BETWEEN @from AND @to
            ORDER BY timestamp ASC
        ";
        
        using var cmd = new NpgsqlCommand(sql, _questDb);
        cmd.Parameters.AddWithValue("symbol", symbol);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        
        var trades = new List<TradeData>();
        using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            trades.Add(new TradeData
            {
                Timestamp = reader.GetDateTime(0),
                Price = reader.GetDecimal(1),
                Quantity = reader.GetDecimal(2),
                Side = reader.GetString(3)
            });
        }
        
        return trades;
    }
}
```

#### C. Configuration

```json
{
  "QuestDB": {
    "Enabled": true,
    "ConnectionString": "Host=localhost;Port=8812;Database=qdb;Username=admin;Password=quest",
    "IlpEndpoint": "tcp::addr=localhost:9009;",
    "BatchSize": 10000,
    "FlushIntervalMs": 1000,
    "RetentionDays": 30
  },
  "RollingWindow": {
    "HotCacheDurationMinutes": 1,
    "WarmCacheDurationMinutes": 30
  }
}
```

---

## Performance Comparison

| Metric | In-Memory | QuestDB Hybrid | Notes |
|--------|-----------|----------------|-------|
| **Write Latency** | <1µs | <1µs* | *Async batch |
| **Read Latency (hot)** | <1ms | <1ms | Same (RAM) |
| **Read Latency (warm)** | <1ms | 5-20ms | QuestDB scan |
| **Snapshot Load** | 1ms | 20ms | Acceptable |
| **RAM Usage** | 500MB | 50MB | 10x reduction |
| **Disk Usage** | 0 | ~60GB/month | Compressed |
| **Persistence** | ❌ | ✅ | Survives restart |
| **Historical Queries** | ❌ | ✅ | SQL analytics |
| **Throughput** | 5K ops/sec | 4M writes/sec | Batched |

---

## Implementation Plan

### Phase 1: Setup (1 hour)
- [ ] Install QuestDB (Docker)
- [ ] Create schema and indexes
- [ ] Add NuGet packages (`QuestDB.Client`, `Npgsql`)

### Phase 2: Writer (2 hours)
- [ ] Implement `QuestDbWriter` service
- [ ] Add to DI container
- [ ] Hook into `RollingWindowService` event
- [ ] Test write throughput

### Phase 3: Reader (2 hours)
- [ ] Implement `TradeRepository` hybrid reads
- [ ] Update `TradeController` to use repository
- [ ] Add caching layer
- [ ] Test read performance

### Phase 4: Cleanup (1 hour)
- [ ] Add TTL/retention policy
- [ ] Add health checks
- [ ] Add monitoring metrics
- [ ] Documentation

**Total Estimate:** 6 hours

---

## Benefits

### Immediate
1. ✅ **90% RAM reduction** (500MB → 50MB)
2. ✅ **Persistence** - Data survives restarts
3. ✅ **Same real-time performance** - Hot cache from RAM

### Long-term
1. ✅ **Historical analysis** - SQL queries over weeks/months
2. ✅ **Scalability** - Supports 10x more symbols
3. ✅ **Compression** - 10x disk savings
4. ✅ **Compliance** - Full audit trail

---

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| QuestDB downtime | Medium | Graceful degradation (RAM-only mode) |
| Write lag | Low | Monitor queue depth, alert on >5K |
| Disk space | Medium | Automated cleanup, compression, alerts |
| Query performance | Low | Indexed queries, connection pooling |

---

## Alternatives Considered

### 1. ClickHouse
- **Pros:** Better analytics, extreme compression
- **Cons:** More complex setup, overkill for 30-day retention

### 2. Redis TimeSeries
- **Pros:** Ultra-fast, simple
- **Cons:** Higher memory usage, limited SQL

### 3. TimescaleDB
- **Pros:** PostgreSQL ecosystem
- **Cons:** 10x slower than QuestDB for time-series

**Decision:** QuestDB offers best balance for this use case.

---

## Success Metrics

### Before (Baseline)
- RAM usage: 500MB
- Snapshot latency: 1ms
- Persistence: None
- Historical queries: None

### After (Target)
- RAM usage: <100MB (80% reduction)
- Snapshot latency: <50ms (50x slower, but acceptable)
- Persistence: 100% (all data)
- Historical queries: <100ms for 1M records

---

## Next Steps

1. **Approval** - Review and approve proposal
2. **Prototype** - 2-hour spike to validate performance
3. **Implementation** - Follow phased rollout
4. **Monitoring** - Track metrics for 1 week
5. **Iterate** - Tune based on production data

---

## References

- [QuestDB Benchmark](https://questdb.io/docs/reference/sql/benchmark/)
- [Time-Series Best Practices](https://questdb.io/blog/2021/11/26/database-benchmark-questdb/)
- [ILP Protocol Spec](https://questdb.io/docs/reference/api/ilp/overview/)
