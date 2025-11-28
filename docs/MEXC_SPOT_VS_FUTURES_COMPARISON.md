# MEXC Spot vs Futures API - Comparison Table

**Date:** 2025-11-28
**Purpose:** SPRINT 0 - Research for MexcFuturesExchangeClient implementation
**Library:** JK.Mexc.Net v3.12.0

---

## Summary of Key Differences

| Aspect | Spot API | Futures API | Impact |
|--------|----------|-------------|---------|
| **Library Support** | ✅ Full | ✅ Full (since v3.4.0) | None |
| **Order Operations** | ✅ Available | ❌ Disabled by MEXC | ⚠️ Can't place orders (OK for screener) |
| **Market Data** | ✅ Available | ✅ Available | ✅ Perfect for screener |
| **WebSocket Streams** | ✅ Available | ✅ Available | ✅ Real-time data OK |

---

## REST API Comparison

### 1. GetSymbolsAsync()

| Feature | Spot API | Futures API |
|---------|----------|-------------|
| **Method** | `SpotApi.ExchangeData.GetExchangeInfoAsync()` | `FuturesApi.ExchangeData.GetSymbolsAsync()` |
| **Return Type** | `WebCallResult<MexcExchangeInfo>` | `WebCallResult<MexcContract[]>` |
| **Symbol Collection** | `.Data.Symbols` | `.Data` (already array) |
| **Symbol Format** | `BTCUSDT` (no separator) | `BTC_USDT` (with underscore) ⚠️ |
| **Price Precision** | `.QuoteAssetPrecision` | `.TickSize` |
| **Quantity Precision** | `.BaseAssetPrecision` | `.LotSize` |
| **Min Notional** | `.QuoteQuantityPrecision` | `.MinQuantity` |

**🔧 Mapping Required:**
```csharp
// Spot
PriceStep = (decimal)Math.Pow(10, -s.QuoteAssetPrecision)
QuantityStep = (decimal)Math.Pow(10, -s.BaseAssetPrecision)
MinNotional = s.QuoteQuantityPrecision

// Futures - need to verify actual property names during implementation
PriceStep = s.TickSize  // Already decimal
QuantityStep = s.LotSize  // Already decimal
MinNotional = s.MinQuantity  // Already decimal
```

---

### 2. GetTickersAsync()

| Feature | Spot API | Futures API |
|---------|----------|-------------|
| **Method** | `SpotApi.ExchangeData.GetTickersAsync()` | `FuturesApi.ExchangeData.GetTickersAsync()` |
| **Return Type** | `WebCallResult<MexcTicker[]>` | `WebCallResult<MexcFuturesTicker[]>` |
| **Volume Field** | `.QuoteVolume` | `.QuoteVolume` ✅ Same |
| **Price Change** | `.PriceChange` (already %) | `.PriceChangePercent` |
| **Last Price** | `.LastPrice` | `.LastPrice` ✅ Same |
| **High/Low** | `.HighPrice`, `.LowPrice` | `.HighPrice`, `.LowPrice` ✅ Same |

**🔧 Mapping Required:**
```csharp
// Spot
Volume24h = t.QuoteVolume ?? 0
PriceChangePercent24h = t.PriceChange  // Already in %

// Futures
Volume24h = t.QuoteVolume ?? 0
PriceChangePercent24h = t.PriceChangePercent  // Already in %
```

---

### 3. GetOrderBookAsync()

| Feature | Spot API | Futures API |
|---------|----------|-------------|
| **Method** | `SpotApi.ExchangeData.GetOrderBookAsync(symbol, limit)` | `FuturesApi.ExchangeData.GetOrderBookAsync(symbol, limit)` |
| **Return Type** | `WebCallResult<MexcOrderBook>` | `WebCallResult<MexcFuturesOrderBook>` |
| **Bids** | `.Data.Bids` | `.Data.Bids` ✅ Same |
| **Asks** | `.Data.Asks` | `.Data.Asks` ✅ Same |
| **Entry Structure** | `.Price`, `.Quantity` | `.Price`, `.Quantity` ✅ Same |

**🔧 Mapping Required:**
```csharp
// Both APIs use same structure
var bestBid = orderbookResult.Data.Bids.First().Price;
var bestAsk = orderbookResult.Data.Asks.First().Price;
```

---

## WebSocket API Comparison

### 4. SubscribeToTradeUpdatesAsync()

| Feature | Spot API | Futures API |
|---------|----------|-------------|
| **Method** | `SpotApi.SubscribeToTradeUpdatesAsync()` | `FuturesApi.SubscribeToTradeUpdatesAsync()` |
| **Symbols Parameter** | `IEnumerable<string> symbols` ✅ Multiple | `string symbol` ⚠️ **Single only!** |
| **Update Interval** | `int interval` (100ms, 1000ms, etc.) | ❌ **Not available** |
| **Callback Type** | `Action<DataEvent<MexcStreamTrade[]>>` | `Action<DataEvent<MexcFuturesTrade[]>>` |
| **Return Type** | `Task<CallResult<UpdateSubscription>>` | `Task<CallResult<UpdateSubscription>>` |

**⚠️ CRITICAL DIFFERENCE:**
- **Spot:** Can subscribe to multiple symbols in one call
- **Futures:** Must subscribe to each symbol **individually**

**🔧 Adapter Implementation Impact:**
```csharp
// SPOT ADAPTER (current - MexcSocketApiAdapter)
public async Task<object> SubscribeToTradeUpdatesAsync(
    IEnumerable<string> symbols,  // Multiple symbols
    Func<TradeData, Task> onData)
{
    var result = await _spotApi.SubscribeToTradeUpdatesAsync(
        symbols,   // Pass all at once
        100,       // Update interval
        async data => { /* process */ });
    return result;
}

// FUTURES ADAPTER (new - MexcFuturesSocketApiAdapter)
public async Task<object> SubscribeToTradeUpdatesAsync(
    IEnumerable<string> symbols,  // Interface requires IEnumerable
    Func<TradeData, Task> onData)
{
    // PROBLEM: Futures API only accepts single symbol
    // SOLUTION: Subscribe to each symbol separately

    var subscriptions = new List<UpdateSubscription>();
    foreach (var symbol in symbols)
    {
        var result = await _futuresApi.SubscribeToTradeUpdatesAsync(
            symbol,  // One at a time!
            async data => { /* process */ });

        if (result.Success)
            subscriptions.Add(result.Data);
    }

    // Return composite object or first subscription
    return subscriptions.FirstOrDefault();
}
```

---

## Implementation Strategy

### Chunk Size Consideration

**Spot API:**
```csharp
protected override int ChunkSize => 6;  // 6 symbols per connection
```

**Futures API:**
```csharp
protected override int ChunkSize => ???  // NEEDS TESTING

// Option 1: Keep ChunkSize = 6
// Create 6 separate subscriptions per ManagedConnection
// Each subscription = 1 symbol

// Option 2: ChunkSize = 1
// Create 1 ManagedConnection per symbol
// Simpler but more connections

// Recommendation: Start with ChunkSize = 1 for simplicity
```

---

## Rate Limits

| API Type | Spot | Futures |
|----------|------|---------|
| **REST Requests** | Unknown | 20 requests / 2 seconds (from code) |
| **WebSocket Connections** | 30 subscriptions per connection | Unknown (needs testing) |
| **Orderbook Refresh** | 10ms delay between calls | 10ms delay between calls (same) |

---

## Symbol Naming Format

**⚠️ CRITICAL: Symbol format appears different!**

| Exchange | Spot Format | Futures Format |
|----------|-------------|----------------|
| MEXC | `BTCUSDT` | `BTC_USDT` ??? |

**🔧 TODO in Sprint 1:**
- Test actual symbol format returned by `GetSymbolsAsync()`
- Verify WebSocket subscription format
- Add conversion if needed:
  ```csharp
  // If conversion needed
  var futuresSymbol = spotSymbol.Insert(spotSymbol.Length - 4, "_");
  // "BTCUSDT" -> "BTC_USDT"
  ```

---

## Data Models (Estimated)

### Spot Models
- `MexcSymbol` - symbol info
- `MexcTicker` - 24h ticker
- `MexcOrderBook` - orderbook
- `MexcStreamTrade` - websocket trade

### Futures Models
- `MexcContract` - contract/symbol info
- `MexcFuturesTicker` - 24h ticker
- `MexcFuturesOrderBook` - orderbook
- `MexcFuturesTrade` - websocket trade

**🔧 Exact properties will be discovered during Sprint 1 implementation**

---

## Recommended Implementation Order (Sprint 1)

1. ✅ **Start with GetSymbolsAsync()** - discover actual data structure
2. ✅ **Then GetTickersAsync()** - verify ticker fields
3. ✅ **Then GetOrderbookAsync()** - test orderbook structure
4. ✅ **Finally WebSocket Adapter** - handle single-symbol subscription pattern
5. ⚠️ **Test ChunkSize** - determine optimal chunk size for Futures

---

## Conclusion

### ✅ Feasible to Implement

**Low Risk:**
- REST API structure is very similar to Spot
- Orderbook format appears identical

**Medium Risk:**
- Symbol naming format might differ (needs testing)
- WebSocket requires **individual subscriptions** (more complex adapter)

**Mitigation:**
- Start Sprint 1 with REST methods first
- Test symbol format early
- Implement WebSocket adapter last
- Use ChunkSize=1 initially for simplicity

---

**Next Steps:** Proceed to SPRINT 1 - Implementation

**Author:** Claude (GEMINI_DEV роль)
**Version:** 1.0
