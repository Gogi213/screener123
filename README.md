# 🚀 MEXC Trade Screener Pro

Real-time monitoring and analysis for 2000+ MEXC trading pairs with advanced metrics.

## ⚡ Quick Start

```bash
cd c:\visual projects\screener123\collections
dotnet build && dotnet run --project src\SpreadAggregator.Presentation
```

Open: **http://localhost:5000/index.html**

---

## 📊 Features

- **Real-time Streaming:** WebSocket connection to MEXC exchange
- **TOP-30 Display:** Most active pairs by trades/3m
- **Advanced Metrics:** Acceleration, bot detection, buy/sell imbalance
- **Performance Optimized:** Scatter-only graphs, 1s batch updates
- **Freeze Control:** Stop sorting to study coins

---

## 🎮 Controls

| Button | State | Description |
|--------|-------|-------------|
| 🔥 Live Sort | Active | Auto re-sort every 10s |
| ❄️ Frozen | Inactive | Freeze to study coins |

---

## 📈 Card Display

```
BTCUSDT              45000
285/3m  ↑2.5x  📊
═══════ Chart ═══════
```

- `285/3m` - Trades in last 3 minutes
- `↑2.5x` - Acceleration (current min / previous min)
  - ⚫ Gray: < 2.0x (normal)
  - 🟠 Orange: 2.0-3.0x (high)
  - 🔴 Red: >= 3.0x (extreme)
- `📊` - Imbalance indicator (if > 0.7)

---

## 📁 Documentation

- **QUICK_START.md** - Resume work in new session
- **SPRINT_CONTEXT.md** - Full sprint history and implementation details
- **ARCHITECTURE.md** - System architecture

---

## 🐛 Troubleshooting

**Charts flickering?**
- Check `isFirstLoad` flag in `screener.js`

**Sorting wrong?**
- Verify `symbolActivity` updated only from WebSocket

**WebSocket disconnects?**
- Reduce chart count (currently TOP-30)

---

## 📝 Tech Stack

- **Backend:** .NET 9, C#
- **Frontend:** Vanilla JS, uPlot charts
- **Communication:** WebSocket (Fleck)
- **Data:** Rolling window (30min), circular buffers

---

**Status:** Production Ready ✅  
**Last Updated:** 2025-11-25
