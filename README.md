# 🚀 MEXC Trade Screener

Real-time monitoring and analysis for MEXC-exclusive trading pairs with advanced metrics.

## ⚡ Quick Start

```bash
cd c:\visual projects\screener123\collections
dotnet build && dotnet run --project src\SpreadAggregator.Presentation
```

Open: **http://localhost:5000/index.html**

---

## 📊 Features

- **Real-time Streaming:** WebSocket connection to MEXC exchange
- **MEXC-Exclusive Filter:** Excludes Binance/Bybit/OKX symbols (~1,200 unique pairs)
- **TOP-30 Display:** Most active pairs by trades/3m
- **Advanced Metrics:** Acceleration, bot detection, buy/sell imbalance
- **Auto-Reconnect:** Resilient WebSocket with exponential backoff
- **Health Monitoring:** Visual alerts for connection issues
- **Performance Optimized:** 2% CPU, 60 MB RAM (stable)

---

## 🎮 Controls

| Button | State | Description |
|--------|-------|-------------|
| 🔥 Live Sort | Active | Auto re-sort every 10s |
| ❄️ Frozen | Inactive | Freeze to study coins |
| Click Symbol | - | Copy to clipboard |

---

## 📈 Card Display

```
BTCUSDT              45000
285/3m  ↑2.5x
═══════ Chart ═══════
```

- `285/3m` - Trades in last 3 minutes
- `↑2.5x` - Acceleration (current min / previous min)
  - ⚫ Gray: < 2.0x (normal)
  - 🟠 Orange: 2.0-3.0x (high)
  - 🔴 Red: >= 3.0x (extreme)
- **Chart:** Green=Buy, Red=Sell (scatter points)

---

## 📁 Documentation

- **[SPRINT_CONTEXT.md](docs/SPRINT_CONTEXT.md)** - Current state, resume for new chat
- **[QUICK_START.md](docs/QUICK_START.md)** - Detailed guide
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - Technical design
- **[GEMINI_DEV.md](docs/GEMINI_DEV.md)** - Development protocol
- **[CHANGELOG.md](CHANGELOG.md)** - Version history

---

## 🛠️ Tech Stack

- **Backend:** ASP.NET Core (.NET 9.0)
- **Exchange Client:** CryptoExchange.Net
- **WebSocket:** Fleck (port 8181)
- **Frontend:** Vanilla JS + uPlot charts
- **Filter:** Public APIs (Binance, Bybit, OKX)

---

## ⚠️ Known Behavior

- **Charts empty on reload** - Fill within 30 seconds as trades arrive
- **Health alert** - Shows if no trades for 30+ seconds (MEXC disconnect)
- **TOP-30 rotation** - Symbols change based on activity (expected)

---

## 📊 Performance

- **CPU:** ~2% (1,200 symbols)
- **RAM:** ~60 MB (stable, no leaks)
- **Symbols:** ~1,200 MEXC-exclusive (filtered from ~2,400 total)
- **Uptime:** Resilient (auto-reconnect on disconnect)

---

**Status:** ✅ Production Ready | **Version:** 1.3.0 (SPRINT-4/5 Complete)
