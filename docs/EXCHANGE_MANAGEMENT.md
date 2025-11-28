# MEXC Exchange Management Guide

**Purpose:** How to enable/disable MEXC Spot and Futures markets

---

## Quick Reference

| Configuration | Spot | Futures | Use Case |
|---------------|------|---------|----------|
| **Spot Only** | ✅ | ❌ | Default, current production |
| **Futures Only** | ❌ | ✅ | Test futures market |
| **Both** | ✅ | ✅ | Full MEXC coverage |

---

## Configuration Files

### 1. DI Registration (Program.cs)

Both clients are **always registered** in `Program.cs`:

```csharp
// MEXC SPOT: Spot trading pairs
services.AddSingleton<IExchangeClient, MexcExchangeClient>();

// MEXC FUTURES: Futures trading pairs
services.AddSingleton<IExchangeClient, MexcFuturesExchangeClient>();
```

**Note:** Registration alone doesn't activate the exchange. It requires configuration in `appsettings.json`.

### 2. Runtime Configuration (appsettings.json)

Exchanges are activated based on `ExchangeSettings:Exchanges` section.

---

## Configuration Examples

### ✅ Spot Only (Default - Current Production)

**File:** `appsettings.json`

```json
{
  "ExchangeSettings": {
    "Exchanges": {
      "Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      }
    }
  }
}
```

**Result:**
- ✅ MEXC Spot active
- ❌ MEXC Futures inactive
- OrchestrationService finds "Mexc" → starts MexcExchangeClient

---

### ⚡ Futures Only

**File:** `appsettings.json`

```json
{
  "ExchangeSettings": {
    "Exchanges": {
      "MexcFutures": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      }
    }
  }
}
```

**Result:**
- ❌ MEXC Spot inactive
- ✅ MEXC Futures active
- OrchestrationService finds "MexcFutures" → starts MexcFuturesExchangeClient

---

### 🔥 Both Spot + Futures

**File:** `appsettings.json`

```json
{
  "ExchangeSettings": {
    "Exchanges": {
      "Mexc": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      },
      "MexcFutures": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 999999999999
        }
      }
    }
  }
}
```

**Result:**
- ✅ MEXC Spot active
- ✅ MEXC Futures active
- OrchestrationService starts BOTH clients

**Note:** Symbols will be combined in the frontend. BinanceSpotFilter applies to both to avoid duplicates.

---

## Volume Filter Settings

### Recommended Values

| Market Type | MinUsdVolume | Rationale |
|-------------|--------------|-----------|
| **Spot** | 100,000 | Filter low-liquidity coins |
| **Futures** | 100,000 | Same as spot (consistent) |
| **Testing** | 1,000 | More symbols for testing |

### Custom Filters

```json
{
  "Mexc": {
    "VolumeFilter": {
      "MinUsdVolume": 50000,   // Lower threshold for spot
      "MaxUsdVolume": 10000000 // Cap very high volume
    }
  },
  "MexcFutures": {
    "VolumeFilter": {
      "MinUsdVolume": 200000,  // Higher threshold for futures
      "MaxUsdVolume": 999999999999
    }
  }
}
```

---

## How OrchestrationService Works

### 1. Exchange Discovery

```csharp
var exchangeNames = _configuration
    .GetSection("ExchangeSettings:Exchanges")
    .GetChildren()
    .Select(x => x.Key);  // ["Mexc", "MexcFutures", ...]
```

### 2. Client Matching

```csharp
foreach (var exchangeName in exchangeNames)
{
    var client = _exchangeClients
        .FirstOrDefault(c => c.ExchangeName.Equals(exchangeName, ...));
    // "Mexc" → MexcExchangeClient (ExchangeName = "Mexc")
    // "MexcFutures" → MexcFuturesExchangeClient (ExchangeName = "MexcFutures")
}
```

### 3. Symbol Filtering

```csharp
if (exchangeName.Equals("MEXC", ...) ||
    exchangeName.Equals("MexcFutures", ...))
{
    // Apply BinanceSpotFilter to avoid duplicates
    filteredSymbols = _binanceSpotFilter.FilterExcludeBinance(symbols);
}
```

---

## Troubleshooting

### Issue: "No client found for exchange: MexcFutures"

**Cause:** `MexcFuturesExchangeClient` not registered in DI

**Solution:** Check `Program.cs` line 99:
```csharp
services.AddSingleton<IExchangeClient, MexcFuturesExchangeClient>();
```

---

### Issue: Futures not starting even with config

**Cause:** ExchangeName mismatch

**Check:**
1. `MexcFuturesExchangeClient.cs`: `public override string ExchangeName => "MexcFutures";`
2. `appsettings.json`: Key must be **exactly** `"MexcFutures"` (case-insensitive)

---

### Issue: Duplicate symbols in frontend

**Cause:** BinanceSpotFilter not applied to Futures

**Solution:** Check `OrchestrationService.cs:177-180`:
```csharp
if (exchangeName.Equals("MEXC", ...) ||
    exchangeName.Equals("MexcFutures", ...))
{
    filteredSymbolNames = _binanceSpotFilter.FilterExcludeBinance(filteredSymbolNames);
}
```

---

## Testing Checklist

### Before Production

- [ ] **Spot Only:** Verify existing functionality unchanged
- [ ] **Futures Only:** Verify futures symbols load
- [ ] **Both:** Verify no duplicate symbols
- [ ] **Volume Filter:** Verify filtering works for both
- [ ] **BinanceFilter:** Verify applied to both
- [ ] **Health Check:** `/health` shows correct exchange status

### Performance

- [ ] CPU usage acceptable with both enabled
- [ ] Memory usage acceptable with both enabled
- [ ] WebSocket connections stable (68 for Spot + X for Futures)
- [ ] No rate limit errors from MEXC API

---

## Migration Path

### Current: Spot Only → Target: Both

**Step 1:** Test Futures alone first
```json
"Exchanges": {
  "MexcFutures": { "VolumeFilter": { "MinUsdVolume": 100000 } }
}
```

**Step 2:** Verify futures work correctly
- Check logs: `[MexcFutures] Loaded X symbols`
- Check frontend: futures symbols visible
- Check WebSocket: subscriptions successful

**Step 3:** Enable both
```json
"Exchanges": {
  "Mexc": { ... },
  "MexcFutures": { ... }
}
```

**Step 4:** Monitor for 24h
- Check for duplicates
- Check performance
- Check error logs

---

## Key Differences: Spot vs Futures

| Feature | Spot | Futures |
|---------|------|---------|
| **Symbol Format** | `BTCUSDT` | `BTC_USDT` (underscore) |
| **Ticker BestBid/Ask** | ❌ No (needs orderbook) | ✅ Yes (in ticker!) |
| **WebSocket Limit** | 6 symbols per connection | 1 symbol per connection |
| **ChunkSize** | 6 | 1 |
| **Orderbook Refresh** | Required every 10s/60s | Optional (bid/ask in ticker) |

---

## Files Modified

### Sprint 2 Changes

1. **Program.cs:99**
   Added: `services.AddSingleton<IExchangeClient, MexcFuturesExchangeClient>();`

2. **appsettings.json**
   Default: Spot only (no changes needed)
   Optional: Add `MexcFutures` section to enable

3. **OrchestrationService.cs:177** (if needed)
   Apply BinanceSpotFilter to "MexcFutures" too

---

**Author:** Claude (GEMINI_DEV роль)
**Date:** 2025-11-28
**Version:** 1.0
