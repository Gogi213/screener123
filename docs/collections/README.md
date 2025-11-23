# Collections Project Documentation

**Project:** SpreadAggregator (Trade Screener)  
**Status:** Production  
**Last Updated:** 2025-11-23  

---

## 📚 Documentation Index

### Quick Start
- [Project Statistics](./PROJECT_STATISTICS.md) - Metrics & overview
- [Audit Summary](./AUDIT/README.md) - Health check & recommendations

### Deep Dive
- [Refactoring Audit](./AUDIT/REFACTORING_AUDIT_2025-11-23.md) - Complete code review
- [Logging Inventory](./AUDIT/LOGGING_INVENTORY.md) - Log analysis & migration plan
- [QuestDB Integration Proposal](./PROPOSALS/QUESTDB_INTEGRATION.md) - Future enhancement

---

## 🎯 Project Overview

**Current Purpose:** Real-time Trade Screener для мониторинга высокообъёмных сделок на криптобиржах.

**Key Features:**
- ✅ Multi-exchange support (Mexc, Binance)
- ✅ Real-time WebSocket streaming
- ✅ Whale trade detection (>$10K)
- ✅ 30-minute rolling windows
- ✅ Modern web UI (Chart.js)

**Legacy Features (Being Removed):**
- ❌ Arbitrage spread calculation
- ❌ Parquet-based analytics
- ❌ Multi-exchange chart dashboard

---

## 📊 Key Metrics

| Metric | Value |
|--------|-------|
| **Total LoC** | 6,109 |
| **C# Files** | 67 |
| **Dead Code** | ~900 lines (15%) |
| **RAM Usage** | 500MB |
| **Throughput** | 5K trades/sec |

---

## 🔍 Audit Findings (2025-11-23)

### Health Score: 🟢 IMPROVED (Was Moderate Debt)

**Top Issues Resolved:**
1. ✅ **Dead Code** - Deleted ~900 lines of legacy code (DashboardController, ParquetReader, etc.).
2. ✅ **Logging Mess** - Migrated to Serilog (Structured JSON + Text).
3. ✅ **Config Issues** - Cleaned appsettings.json.
4. ✅ **Build Stability** - Fixed all build errors and warnings.

**Remaining Tasks:**
1. 🔜 **Persistence** - Add QuestDB (Phase 4).
2. 🟡 **Architecture** - Split RollingWindowService (Phase 3 - partially done).

**Recommended Actions:**
1. 🚀 **Proceed to QuestDB Integration**

---

## 📁 Documentation Structure

```
docs/collections/
├── README.md                           # This file
├── HANDOVER_CONTEXT.md                 # 🚀 START HERE for new chat
├── PROJECT_STATISTICS.md               # Metrics & breakdown
├── AUDIT/
│   ├── README.md                       # Audit summary
│   ├── REFACTORING_AUDIT_2025-11-23.md # Detailed findings
│   └── LOGGING_INVENTORY.md            # Log analysis
└── PROPOSALS/
    └── QUESTDB_INTEGRATION.md          # DB persistence proposal
```

---

## 🚀 Quick Links

### For Developers
- [Refactoring Roadmap](./AUDIT/REFACTORING_AUDIT_2025-11-23.md#refactoring-roadmap)
- [Code to Delete](./AUDIT/REFACTORING_AUDIT_2025-11-23.md#deletion-candidates)
- [Logging Migration](./AUDIT/LOGGING_INVENTORY.md#migration-plan)

### For Architects
- [Architecture Issues](./AUDIT/REFACTORING_AUDIT_2025-11-23.md#architectural-issues)
- [QuestDB Proposal](./PROPOSALS/QUESTDB_INTEGRATION.md)
- [Performance Metrics](./PROJECT_STATISTICS.md#performance-characteristics)

### For DevOps
- [Log Files](./AUDIT/LOGGING_INVENTORY.md#log-file-inventory)
- [Resource Usage](./PROJECT_STATISTICS.md#resource-usage)
- [Dependencies](./PROJECT_STATISTICS.md#dependencies)

---

## 🎯 Refactoring Timeline

### Phase 1: Cleanup (Week 1)
- **Effort:** 8 hours
- **Delete:** DashboardController, ParquetReaderService, WebSocketLogger
- **Clean:** Configuration, mock data

### Phase 2: Logging (Week 2)
- **Effort:** 6 hours
- **Install:** Serilog
- **Remove:** Custom loggers
- **Implement:** Structured logging

### Phase 3: Architecture (Weeks 3-4)
- **Effort:** 16 hours
- **Split:** RollingWindowService
- **Deprecate:** RealTimeController
- **Document:** Data flows

### Phase 4: QuestDB (Weeks 5-8)
- **Effort:** 24 hours
- **Setup:** Docker, schema
- **Implement:** Hybrid read/write
- **Optimize:** Query performance

**Total:** 54 hours over 8 weeks

---

## 💾 QuestDB Integration (Proposed)

**Benefits:**
- 🚀 **90% RAM reduction** (500MB → 50MB)
- 💾 **Persistence** (survives restarts)
- 📊 **Historical queries** (SQL analytics)
- ⚡ **Same performance** (batched writes)

**See:** [QUESTDB_INTEGRATION.md](./PROPOSALS/QUESTDB_INTEGRATION.md)

---

## 📝 Recent Changes

### 2025-11-23
- ✅ Complete refactoring audit
- ✅ Logging inventory
- ✅ QuestDB proposal
- ✅ Project statistics
- ✅ Fixed screener.html (trades/1m display)
- ✅ Filtered API to MEXC only

### 2025-11-22
- ✅ Migrated from index.html → screener.html
- ✅ Fixed WebSocket dynamic host
- ✅ Enabled Binance + Tickers
- ✅ Registered OpportunityFilterService

---

## 🐛 Known Issues

1. **No log rotation** - Files grow unbounded
2. **Mock CSV data** - OpportunityFilterService uses hardcoded file
3. **Mixed architecture** - Spread + Trade services combined
4. **No persistence** - Data lost on restart

**Status:** All tracked in [REFACTORING_AUDIT](./AUDIT/REFACTORING_AUDIT_2025-11-23.md)

---

## 📚 External Resources

- [QuestDB Documentation](https://questdb.io/docs/)
- [Serilog Best Practices](https://github.com/serilog/serilog/wiki/Best-Practices)
- [ASP.NET Core Performance](https://learn.microsoft.com/en-us/aspnet/core/performance/)

---

## 🔧 Development Setup

```bash
# Clone repo
git clone <repo-url>
cd screener123/collections

# Build
dotnet build

# Run
dotnet run --project src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj

# Access
http://localhost:5000/
```

---

## 📞 Contact

For questions about this documentation or the audit:
- See [REFACTORING_AUDIT](./AUDIT/REFACTORING_AUDIT_2025-11-23.md)
- Check [LOGGING_INVENTORY](./AUDIT/LOGGING_INVENTORY.md)
- Review [QuestDB Proposal](./PROPOSALS/QUESTDB_INTEGRATION.md)

---

**Generated by:** Sequential Thinking Analysis  
**Version:** 1.0.0  
**Date:** 2025-11-23
