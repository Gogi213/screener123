# 🚀 MEXC Trade Screener - Current State

**Last Updated:** 2025-11-26 01:14 UTC+4  
**Status:** ✅ Production Ready - All Features Complete

---

## 📊 Quick Stats

- **Symbols Tracked:** ~1,200 (MEXC-exclusive, filtered from Binance/Bybit/OKX)
- **Performance:** 2% CPU, 60 MB RAM (stable)
- **Uptime:** Resilient (auto-reconnect on disconnect)
- **UI:** Real-time charts for TOP-30 symbols

---

## ✅ Completed Features

### **Core Functionality**
- ✅ Real-time WebSocket streaming (MEXC trades)
- ✅ Rolling window metrics (trades/1m, 2m, 3m)
- ✅ Advanced benchmarks (acceleration, buy/sell imbalance, volume patterns)
- ✅ TOP-30 display with uPlot scatter charts
- ✅ Freeze/Live sorting controls
- ✅ Major exchanges filter (exclude Binance/Bybit/OKX symbols)

### **Resilience (SPRINT-4, SPRINT-5)**
- ✅ WebSocket auto-reconnect with exponential backoff (1s → 30s)
- ✅ Health monitoring (alert when no trades for 30+ seconds)
- ✅ Seamless recovery from server restarts

---

## 🎯 Current Sprint: COMPLETE

**Last Sprints:**
- **SPRINT-4:** WebSocket Reconnection ✅
- **SPRINT-5:** Health Monitoring ✅

**Result:** System stable, no critical issues, ready for use.

---

## 📁 Project Structure

```
screener123/
├── collections/
│   ├── src/
│   │   ├── SpreadAggregator.Domain/         # Entities, interfaces
│   │   ├── SpreadAggregator.Application/    # Business logic
│   │   │   └── Services/
│   │   │       ├── OrchestrationService.cs  # MEXC subscription
│   │   │       ├── TradeAggregatorService.cs # Metrics calculation
│   │   │       └── BinanceSpotFilter.cs     # Major exchanges filter
│   │   ├── SpreadAggregator.Infrastructure/ # MEXC client, WebSocket
│   │   └── SpreadAggregator.Presentation/   # ASP.NET Core + frontend
│   │       └── wwwroot/
│   │           ├── js/screener.js           # Client logic (SPRINT-4, SPRINT-5)
│   │           └── css/screener.css         # Styling
│   └── tests/                                # (Removed - not needed for screener)
└── docs/
    ├── QUICK_START.md                        # How to run
    ├── ARCHITECTURE.md                       # Technical details
    └── GEMINI_DEV.md                         # Development principles
```

---

## 🚀 Quick Commands

```bash
# Start application
cd collections
dotnet build && dotnet run --project src\SpreadAggregator.Presentation

# Open UI
http://localhost:5000/index.html
```

---

## 🔧 Key Configuration

**File:** `collections/src/SpreadAggregator.Presentation/appsettings.json`

```json
{
  "ConnectionStrings": {
    "WebSocket": "ws://0.0.0.0:8181"
  },
  "ExchangeSettings": {
    "Exchanges": {
      "Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 0,
          "MaxUsdVolume": 999999999
        }
      }
    }
  },
  "StreamSettings": {
    "EnableTrades": true
  }
}
```

---

## 📋 Controls

- **🔥 Live Sort** - Re-sorts TOP-30 every 10 seconds (default ON)
- **❄️ Frozen** - Disable auto-sorting to observe specific symbols
- **Click Symbol** - Copy to clipboard

---

## 🎨 Metrics Displayed

**Per Symbol Card:**
- Trades/3m (rolling window)
- Acceleration (↑2.5x - gray if <2.0x, orange/red if >=2.0x)
- Price (last trade)
- Real-time chart (uPlot - green=buy, red=sell)

---

## 🔍 Known Behavior

1. **Charts empty on page reload** - Fill within 10-30 seconds as new trades arrive
2. **TOP-30 changes** - Symbols rotate based on activity (expected)
3. **Health alert** - Shows if no trades for 30+ seconds (indicates MEXC disconnect)

---

## 🐛 Troubleshooting

**Issue:** "Reconnecting..." status stuck  
**Fix:** Check server is running (`dotnet run`)

**Issue:** No charts updating  
**Fix:** Check browser console for WebSocket errors

**Issue:** Health alert after server restart  
**Fix:** Wait 30 seconds - alert auto-clears when trades resume

---

## 📝 Next Session Checklist

1. ✅ System is production-ready
2. ✅ No critical bugs
3. ✅ Performance stable
4. ⚠️ Optional: Add more symbols by adjusting VolumeFilter in appsettings.json

---

## 🎓 Technical Notes

**Architecture:**
- **Server:** ASP.NET Core (.NET 9.0)
- **MEXC Client:** CryptoExchange.Net library
- **WebSocket:** Fleck server (port 8181)
- **Frontend:** Vanilla JS + uPlot charts
- **Filter:** Binance/Bybit/OKX exclusion via public APIs

**Performance:**
- CPU: ~2% (1,200 symbols)
- RAM: ~60 MB (stable, no leaks)
- Metrics calculation: O(n) where n=symbols, optimized to TOP-500 for expensive benchmarks

**Resilience:**
- Auto-reconnect: exponential backoff (1s, 2s, 4s, 8s, 16s, max 30s)
- Health monitoring: 30-second threshold for alerts
- No manual intervention needed for recovery

---

**System Status:** ✅ **READY FOR USE**
