# SpreadAggregator - Collections Project

**Status:** ✅ Active & Refactored (2025-11-23)
**Type:** ASP.NET Core 9.0 Web Application
**Focus:** Real-time Trade Screener (Whale Detection)

---

## 📚 Documentation

Full documentation is located in the `docs/collections` directory:

*   [**Main Documentation**](../docs/collections/README.md) - Start here
*   [**Handover Context**](../docs/collections/HANDOVER_CONTEXT.md) - For new developers
*   [**Refactoring Audit**](../docs/collections/AUDIT/REFACTORING_AUDIT_2025-11-23.md) - Recent changes
*   [**QuestDB Proposal**](../docs/collections/PROPOSALS/QUESTDB_INTEGRATION.md) - Next steps

---

## 🚀 Quick Start

### Build
```bash
dotnet build src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj
```

### Run
```bash
dotnet run --project src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj
```

### Access
*   **Dashboard:** `http://localhost:5000/screener.html`
*   **Logs:** `logs/app.log` (Text) or `logs/app.jsonl` (JSON)

---

## 🏗️ Architecture

*   **Frontend:** Chart.js (screener.html)
*   **Backend:** ASP.NET Core (OrchestrationService, RollingWindowService)
*   **Logging:** Serilog (Structured)
*   **Persistence:** In-Memory (Moving to QuestDB)

---

## ⚠️ Recent Changes (2025-11-23)

*   Removed legacy code (DashboardController, ParquetReader).
*   Integrated Serilog for structured logging.
*   Fixed build errors and warnings.
*   Cleaned configuration.

See [Refactoring Audit](../docs/collections/AUDIT/REFACTORING_AUDIT_2025-11-23.md) for details.
