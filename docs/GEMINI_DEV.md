# GEMINI DEVELOPMENT PROTOCOL

## 1. Core Principles

### 1.1 Evidence-Based Development
- **Sequential Thinking:** Always validate complex decisions before coding.
- **Measure First:** Don't optimize without metrics (CPU, RAM, Network).
- **Fail Fast:** Crash on critical errors, recover from transient ones.

### 1.2 Architecture: Centralized & Simple
- **One Source of Truth:** Centralized monitoring, centralized state.
- **No Over-Engineering:** Use simple solutions (e.g., uPlot vs complex libs).
- **Server-Side Aggregation:** Heavy lifting on backend (OHLCV), lightweight frontend.

---

## 2. System Architecture (Current)

### 2.1 Backend (ASP.NET Core)
- **OrchestrationService:** Manages MEXC subscriptions.
- **TradeAggregatorService:** 
  - Collects trades into 200ms buckets.
  - Computes OHLCV (Open, High, Low, Close, Volume).
  - Broadcasts `trade_aggregate` messages.
- **FleckWebSocketServer:** Handles client connections (port 8181).

### 2.2 Frontend (Vanilla JS + uPlot)
- **screener.js:**
  - Connects to WebSocket.
  - Handles `trade_aggregate` (converts to pseudo-trades).
  - Throttles rendering (1000ms).
  - Manages chart lifecycle (anti-flicker, smart sort).
- **uPlot:** High-performance scatter charts.

### 2.3 Data Flow
1. **MEXC** sends raw trades.
2. **Backend** buffers trades for 200ms.
3. **Backend** computes OHLCV aggregate.
4. **Backend** sends 1 JSON message per symbol.
5. **Frontend** receives aggregate and renders point.

---

## 3. Performance Standards

- **Network:** Minimized via server-side aggregation (-98% traffic).
- **CPU:** Low usage (efficient JSON serialization).
- **Latency:** ~200ms (aggregation window) + network.
- **Frontend:** Throttled rendering (1fps) to prevent freezing.

---

## 4. Workflow

1. **Check `SPRINT_CONTEXT.md`** for current status.
2. **Follow `CHANGELOG.md`** for history.
3. **Use Sequential Thinking** for new features.

---

**Status:** Production Ready (Sprint 9 Complete)
