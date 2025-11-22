# Phase 1: Mexc Trade Screener

**Goal:** Transform the application into a dedicated Trade Screener for Mexc exchange.

**Status:** ✅ COMPLETE
**Start Date:** 2025-11-22

---

## Sprint 1: Mexc Connectivity ✅ COMPLETE
**Goal:** Establish live trade data stream from Mexc.

- [x] **TASK-1.1: Implement Trade Stream**
  - [x] Update `MexcExchangeClient` to support `SubscribeToTradeUpdatesAsync`.
  - [x] Verify `SupportsTradesStream = true`.
  - [x] Test connection and data receipt (Console output).

---

## Sprint 2: System Cleanup ✅ COMPLETE
**Goal:** Configure system for Screener mode (Low Latency, No Disk I/O).

- [x] **TASK-2.1: Configuration**
  - [x] Update `appsettings.json`: Enable Mexc only, Trades only, Disable Recording.
  
- [x] **TASK-2.2: Code Cleanup**
  - [x] Remove `ParquetDataWriter` from `Program.cs`.
  - [x] Remove `BidAskLogger` and `BidBidLogger`.
  - [x] Verify system runs stable without disk writers.

---

## Sprint 3: Trade Aggregation ✅ COMPLETE
**Goal:** Aggregate trades in memory (Rolling Window).

- [x] **TASK-3.1: Handle Trade Data**
  - [x] Update `RollingWindowService.ProcessData` to accept `TradeData`.
  - [x] Implement `AddTradeToWindow` logic.

- [x] **TASK-3.2: Sliding Window**
  - [x] Implement time-based eviction (30 mins).
  - [x] Ensure thread safety for `Trades` queue.

---

## Sprint 4: API & Warm-up ✅ COMPLETE
**Goal:** Expose data via WebSocket API.

- [x] **TASK-4.1: Backend API**
  - [x] Create `TradeController`.
  - [x] Implement `GET /api/trades/symbols`.
  - [x] Implement WebSocket `/ws/trades/{symbol}` with throttling (2 msg/sec).

---

## Sprint 5: Web Visualization ✅ COMPLETE
**Goal:** Visualize data in browser.

- [x] **TASK-5.1: Frontend Setup**
  - [x] Create `wwwroot/screener.html`.
  - [x] Integrate Chart.js (Scatter plot).

- [x] **TASK-5.2: Data Integration**
  - [x] Connect Frontend to Backend (WebSocket).
  - [x] Render Price chart and Trade list.
