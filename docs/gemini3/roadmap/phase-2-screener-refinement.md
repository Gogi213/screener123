# Phase 2: Screener Refinement & UX

**Goal:** Polish the Screener UI, enhance user experience, and add advanced filtering/sorting capabilities.

**Status:** 🟡 IN PROGRESS
**Start Date:** 2025-11-22

---

## Sprint 1: UI/UX Overhaul ✅ COMPLETE
**Goal:** Make the UI professional, readable, and interactive.

- [x] **TASK-1.1: Premium Design**
  - [x] Dark theme (Zinc palette).
  - [x] Typography (Inter + JetBrains Mono).
  - [x] Glassmorphism header.
  - [x] Responsive Grid layout.

- [x] **TASK-1.2: Chart Interactivity**
  - [x] Zoom (Wheel/Pinch).
  - [x] Pan (Drag, requires zoom-in).
  - [x] Double-click to reset.
  - [x] Tooltips with custom formatting.

- [x] **TASK-1.3: Data Visualization**
  - [x] Price Formatter for low-cap coins (`0.(5)123`).
  - [x] Dynamic Trade Counter (updates in real-time).
  - [x] 30-minute rolling window visualization.

- [x] **TASK-1.4: Code Refactoring**
  - [x] Split `screener.html` into HTML/CSS/JS modules.
  - [x] Clean up global namespace.

---

## Sprint 2: Advanced Features ⏳ PENDING
**Goal:** Add tools for finding opportunities faster.

- [ ] **TASK-2.1: Client-Side Filtering**
  - [ ] Search bar for symbols (e.g., "BTC", "PEPE").
  - [ ] Filter by Min/Max Trade Count.

- [ ] **TASK-2.2: Real-Time Sorting**
  - [ ] Re-sort grid based on Activity (Trade Count) dynamically.
  - [ ] Visual indicators of rank change (optional).

- [ ] **TASK-2.3: Volume Analysis**
  - [ ] Calculate Buy/Sell Volume in backend (`RollingWindowService`).
  - [ ] Expose via API.
  - [ ] Display Volume on Card (e.g., "Vol: $1.2M").

---

## Sprint 3: Performance & Stability ⏳ PENDING
**Goal:** Ensure stability under high load (100+ charts).

- [ ] **TASK-3.1: Rendering Optimization**
  - [ ] Monitor FPS with 100 active charts.
  - [ ] Consider `Lightweight Charts` if Chart.js lags.
  - [ ] Implement virtualization (render only visible charts) if needed.

- [ ] **TASK-3.2: Backend Optimization**
  - [ ] Monitor GC pressure from WebSocket broadcasting.
  - [ ] Optimize `GetTrades` snapshot generation (caching?).
