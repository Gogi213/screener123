# Контекст Проекта: Mexc Trade Screener

**Дата анализа:** 2025-11-24  
**Анализ провёл:** Gemini (роль: HFT Development Engineer)  
**Метод:** Sequential Thinking Consilium (sq-анализ)

---

## 🎯 ЧТО ЭТО ЗА ПРОЕКТ

### Назначение
**Mexc Trade Screener** — Real-time система мониторинга торговой активности на бирже Mexc.

**НЕ является HFT-системой!** Это инструмент для наблюдения за торговой активностью с целью выявления интересных паттернов (whale trades, pump activity, volume spikes).

### Технический Стек
- **Backend:** ASP.NET Core 8.0 (монолит)
- **Frontend:** Vanilla JavaScript + Chart.js + chartjs-plugin-zoom
- **Exchange Integration:** Mexc.Net SDK (WebSocket)
- **Data Flow:** System.Threading.Channels (async pipelines)
- **Memory Management:** Custom LruCache с bounded capacity

### Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│ MEXC Exchange (WebSocket)                                       │
│   ↓ Trade Stream (~1000 symbols)                                │
│ OrchestrationService                                            │
│   ↓ Channel.TryWrite (hot path)                                 │
│ RollingWindowChannel (BoundedChannel, 1M capacity)              │
│   ↓ await foreach ReadAllAsync                                  │
│ RollingWindowService (30-min sliding window)                    │
│   ↓ TradeAdded Event (global)                                   │
│ TradeController (/ws/trades/{symbol})                           │
│   ↓ WebSocket streaming (per-symbol)                            │
│ Frontend (screener.html)                                        │
│   ↓ Chart.js scatter plot (Buy/Sell visualization)             │
└─────────────────────────────────────────────────────────────────┘
```

**Clean Architecture Pattern:**
- `Domain`: Entities (TradeData, SpreadData, MarketData)
- `Application`: Services (RollingWindowService, OrchestrationService)  
- `Infrastructure`: Exchange Clients (MexcExchangeClient, etc.)
- `Presentation`: Controllers (TradeController), Frontend (screener.html)

---

## ✅ ТЕКУЩИЙ СТАТУС

### Roadmap Progress
- ✅ **Phase 0:** Foundation (Monolith, DI, Config)
- ✅ **Phase 1:** Screener MVP (Mexc connectivity, Rolling Window, WebSocket API, Charts)
- 🟡 **Phase 2 (IN PROGRESS):** Screener Refinement & UX
  - ✅ Sprint 1: UI/UX Overhaul (Premium Design, Pan/Zoom, Modules)
  - ⏳ Sprint 2: Advanced Features (Search, Sorting, Volume) **PENDING**
  - ⏳ Sprint 3: Performance Tuning **PENDING**

### Что Работает ✅
1. **Real-time Trade Streaming** с Mexc (~1000 символов)
2. **WebSocket API** для frontend (/ws/trades/{symbol})
3. **30-min Rolling Window** для каждого символа
4. **Premium UI/UX:**
   - Dark theme (Zinc palette)
   - Premium typography (Inter + JetBrains Mono)
   - Chart interactivity (Zoom, Pan, Double-click reset)
   - Fancy price formatting (`0.0₅123` for low-cap coins)
   - Real-time Buy/Sell pressure indicators
5. **Pagination** (100 cards/page)
6. **Blacklist** для крупных пар (BTC, ETH, etc.)

### Оптимизации (выполнены)
**Round 1 (2025-11-23):** Pipeline Cleanup
- ❌ Removed DataCollectorService (no-op)
- ❌ Removed TradeScreenerService (file I/O whale logging)
- ❌ Removed legacy WebSocket broadcast (port 8181)
- ❌ Removed RawDataChannel writes
- ❌ Removed TradeScreenerChannel writes

**Round 2 (2025-11-24):** Aggressive Cleanup
- ❌ Disabled PerformanceMonitor (file I/O every 1 second)
- ❌ Disabled FleckWebSocketServer.Start() (legacy socket on 8181)
- ❌ Disabled ExchangeHealthMonitor (timer every 10 seconds)

**Frontend Fixes (2025-11-24):**
- ✅ Fixed WebSocket Reconnect Leak (explicit close)
- ✅ Fixed Chart Data Filter Leak (shift() instead of filter())
- ✅ Fixed updateCardStats Leak (manual count instead of filter())

**Combined Impact:**
- CPU: -15-20% (eliminated background noise)
- Memory: Stable (no frontend leaks)
- File I/O: ZERO ✅
- Background Services: 2 (OrchestrationServiceHost, RollingWindowServiceHost)

---

## 🔴 КРИТИЧЕСКИЕ ПРОБЛЕМЫ ДЛЯ PRODUCTION

### 1. **Global Event Handler Overhead** 🔴 BLOCKER

**Файл:** `TradeController.cs:89`

**Проблема:**
```csharp
_rollingWindow.TradeAdded += handler; // GLOBAL event!

EventHandler<TradeAddedEventArgs> handler = async (sender, e) =>
{
    if (e.Symbol != symbol) return; // ← 99% вызовов отброшены!
    await SendSingleTradeAsync(webSocket, e.Trade, sendLock);
};
```

**Impact:**
- При 100 активных WebSocket (100 символов)
- Каждый trade вызывает 100 handlers
- 99 handlers сразу return (if check)
- При 1000 trades/sec → **100,000 function calls/sec** (99% waste CPU)

**Fix:** Использовать per-symbol event routing вместо global event.

**RFC:** `RollingWindowService` уже имеет `SubscribeToWindow()` для targeted events (строка 329), но TradeController не использует его!

---

### 2. **Safety Cap = 100,000 Trades** 🔴 BLOCKER

**Файл:** `RollingWindowService.cs:272-276`

**Проблема:**
```csharp
// Safety cap: prevent unbounded growth if timestamps are weird
while (window.Trades.Count > 100_000)
{
    window.Trades.Dequeue();
    removedCount++;
}
```

**Impact:**
- При 1000 символов × 100K trades = **10 миллиардов трейдов** в памяти!
- Реальнозубой ориентир: 30 мин × 10 trades/sec = **18,000 trades**
- Safety cap должен быть ~10,000-20,000, а не 100,000

**Fix:** Reduce cap to 10,000 or 20,000

---

### 3. **Hardcoded Paths** 🔴 BLOCKER

**Файл:** `appsettings.json:22, 25, 81`

**Проблема:**
```json
"DataLake": {
    "Path": "C:\\visual projects\\arb1\\data\\market_data"
},
"Analyzer": {
    "StatsPath": "C:\\visual projects\\arb1\\analyzer\\summary_stats"
}
```

**Impact:**
- Не portable (несовместимо с Docker, Linux, другими машинами)
- Hardcoded пути к старому проекту "arb1"

**Fix:**
- Использовать environment variables
- Или relative paths от рабочей директории
- Или вообще удалить (Recording.Enabled=false, не используются)

---

## 🟡 КРИТИЧНЫЕ УЛУЧШЕНИЯ (STRONGLY RECOMMENDED)

### 4. **Нет Health Check Endpoint** 🟡

**Проблема:**
- Невозможно проверить, что приложение живо и работает корректно
- Нет endpoint для мониторинга (Kubernetes liveness/readiness)

**Fix:**
```csharp
// HealthController.cs
[HttpGet("/health")]
public IActionResult GetHealth()
{
    var exchangeHealth = _orchestrationService.GetExchangeHealth();
    var windowCount = _rollingWindow.GetWindowCount();
    
    return Ok(new
    {
        status = "healthy",
        exchanges = exchangeHealth,
        windows = windowCount,
        timestamp = DateTime.UtcNow
    });
}
```

---

### 5. **Подписка на ВСЕ Пары Mexc** 🟡

**Файл:** `appsettings.json:36-39`

**Проблема:**
```json
"Mexc": {
    "VolumeFilter": {
        "MinUsdVolume": 0,  // ← Нет фильтра!
        "MaxUsdVolume": 999999999999
    }
}
```

**Impact:**
- Подписывается на ~800-1200 торговых пар
- Многие — shitcoins с малым объёмом (noise)
- Избыточная нагрузка для MVP screener

**Fix:**
- Установить MinUsdVolume: 100,000 (фильтр малоликвидных пар)
- Или топ-200 по объёму

---

### 6. **Нет Metrics/Observability** 🟡

**Проблема:**
- Нет Prometheus metrics
- Нет OpenTelemetry traces
- Невозможно мониторить production (CPU, Memory, Latency, Event Rate)

**Fix:**
- Добавить `OpenTelemetry` SDK
- Expose `/metrics` endpoint для Prometheus
- Track: `events_processed_total`, `cpu_usage`, `memory_bytes`, `websocket_connections`

---

### 7. **Нет Rate Limiting для WebSocket** 🟡

**Файл:** `TradeController.cs:81`

**Проблема:**
```csharp
await SendSingleTradeAsync(webSocket, e.Trade, sendLock);
// ← Нет throttling! При 100 trades/sec → 100 messages/sec
```

**Impact:**
- Frontend может быть перегружен при высокой активности
- Browser может зафризить

**Fix:**
- Добавить batch sending (группировать trades по 100ms)
- Или rate limiter (max 20 messages/sec)

---

## 🟢 NICE TO HAVE (Дополнительные улучшения)

### 8. Per-Symbol Targeted Events

**RFC:** Использовать `SubscribeToWindow()` вместо global `TradeAdded`:
```csharp
// TradeController.cs (FIX)
var windowKey = $"MEXC_{symbol}";
_rollingWindow.SubscribeToWindow(symbol, "MEXC", "MEXC", handler);
```

### 9. WebSocket Reconnection с Exponential Backoff

**Frontend:** `screener.js:203` использует fixed 3sec delay. Better: exponential backoff (3s → 6s → 12s → 30s).

### 10. Frontend Error Boundary

React-style error boundary для graceful degradation при ошибках в frontend.

### 11. Structured Logging

Использовать structured logging (Serilog) для production-ready логов.

---

## 🔧 PRODUCTION READINESS CHECKLIST

### БЛОКЕРЫ (MUST FIX):
- [ ] 🔴 Fix Global Event Handler (TradeController) → per-symbol routing
- [ ] 🔴 Reduce Safety Cap (100K → 10K-20K)
- [ ] 🔴 Remove Hardcoded Paths (environment variables)

### КРИТИЧНЫЕ (STRONGLY RECOMMENDED):
- [ ] 🟡 Add Health Check endpoint (/health)
- [ ] 🟡 Add Volume Filter (MinUsdVolume: 100K)
- [ ] 🟡 Add Metrics/Observability (Prometheus)
- [ ] 🟡 Add Rate Limiting для WebSocket

### NICE TO HAVE:
- [ ] 🟢 Per-symbol targeted events
- [ ] 🟢 WebSocket reconnection backoff
- [ ] 🟢 Frontend error boundary
- [ ] 🟢 Structured logging

---

## ⏱️ ESTIMATED TIME TO PRODUCTION

| Категория | Tasks | Estimated Time |
|-----------|-------|----------------|
| Блокеры | 3 | 2-3 hours |
| Критичные | 4 | 4-6 hours |
| Nice to Have | 4 | 8-10 hours |
| **TOTAL** | **11** | **12-19 hours** |

---

## 📊 АРХИТЕКТУРНАЯ ОЦЕНКА

### Сильные Стороны ✅
1. **Clean Architecture:** Domain/Application/Infrastructure/Presentation separation
2. **DI Container:** Proper dependency injection
3. **Channel-based Processing:** Modern async pipelines
4. **Memory Safety:** LruCache с bounded capacity (10,000 windows)
5. **Thread-safety:** Locks для concurrent collections
6. **Error Isolation:** Exchange failures не крашат систему
7. **Graceful Shutdown:** Корректная cleanup логика

### Слабые Стороны ⚠️
1. **Global Events:** Вместо targeted routing (99% CPU waste)
2. **Safety Caps Too High:** 100K вместо 10K (риск OOM)
3. **No Observability:** Нет health checks, metrics, traces
4. **Hardcoded Config:** Пути к старому проекту "arb1"
5. **No Rate Limiting:** WebSocket может спамить frontend

### Общая Оценка

**Архитектура:** 8/10 (solid, но есть performance bottlenecks)  
**Code Quality:** 7/10 (чистый код, но нет observability)  
**Production Readiness:** 6/10 (работает, но нужны фикс блокеров)

---

## 🎓 КЛЮЧЕВЫЕ ВЫВОДЫ

1. **Проект функционален** — работает, UI качественный, frontend leaks исправлены
2. **Архитектура solid** — Clean Architecture, DI, Channels, Memory safety
3. **НЕ production-ready** — нужны фиксы блокеров (Events, Safety Cap, Config)
4. **Observability ZERO** — нет health checks, metrics, traces (критично для прода)
5. **Performance bottleneck** — Global Event Handler → 99% CPU waste

**Рекомендация:** Исправить 3 блокера (2-3 hours), добавить базовую observability (health checks, metrics) → можно выкатывать в production.

---

## 📝 ДОПОЛНИТЕЛЬНАЯ ИНФОРМАЦИЯ

### Конфигурация
- **Mexc:** Только одна биржа активна (все остальные закомментированы)
- **EnableTickers:** `false` (отключены)
- **EnableTrades:** `true` (активны)
- **Recording:** `false` (Parquet logging disabled)

### Активные Сервисы
1. `OrchestrationServiceHost` — Подключение к Mexc exchange
2. `RollingWindowServiceHost` — 30-min rolling window maintenance

### Активные Timers (Противоречие!)
**ВНИМАНИЕ:** RollingWindowService.cs имеет 2 активных таймера:
- `_cleanupTimer`: каждые 5 минут (строка 62)
- `_lastTickCleanupTimer`: каждые 2 минуты (строка 63)

Это противоречит заявлению из AGGRESSIVE_CLEANUP_2025-11-24.md: "ZERO active timers". 

**Вердикт:** Документация устарела — timers НЕ disabled!

---

## 🚀 NEXT STEPS

### Immediate (Before Production):
1. Fix Global Event Handler (TradeController)
2. Reduce Safety Cap (100K → 10K)
3. Remove Hardcoded Paths

### High Priority:
4. Add Health Check endpoint
5. Add Volume Filter (MinUsdVolume: 100K)
6. Add Metrics (Prometheus/OpenTelemetry)

### Medium Priority:
7. Add Rate Limiting для WebSocket
8. Per-symbol targeted events
9. WebSocket reconnection backoff

### Low Priority:
10. Frontend error boundary
11. Structured logging (Serilog)

---

**Prepared by:** Gemini (HFT Development Engineer)  
**Analysis Method:** Sequential Thinking Consilium (sq-анализ)  
**Date:** 2025-11-24
