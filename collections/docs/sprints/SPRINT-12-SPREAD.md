# SPRINT-12: Spread (Bid-Ask) Tracking

**Status:** 📋 Planned
**Created:** 2025-11-26
**Priority:** High
**Complexity:** Medium

---

## 🎯 Objective

Implement real-time bid-ask spread tracking to identify tight spread opportunities for high-frequency trading and arbitrage.

---

## 📊 Background

**Spread Formula:**
```
Spread (%) = ((Ask - Bid) / Ask) * 100
Spread (absolute) = Ask - Bid
```

**Why Spread?**
- Tight spreads = High liquidity = Better fill prices
- Spreads widen during volatility spikes (opportunity detection)
- Essential metric for market making and scalping strategies

**Data Source:**
- MEXC provides ticker updates with `BestBid` and `BestAsk` prices
- Alternatively, can be calculated from orderbook depth

---

## 🏗️ Implementation Plan

### **Backend Changes**

1. **MexcExchangeClient.cs** (Data Collection)
   - Modify `GetTickersAsync()` to extract bid/ask:
     ```csharp
     return new TickerData
     {
         Symbol = t.Symbol,
         QuoteVolume = t.QuoteVolume ?? 0,
         Volume24h = t.QuoteVolume ?? 0,
         PriceChangePercent24h = priceChangePercent,
         LastPrice = t.LastPrice,
         BestBid = t.BestBid ?? 0,      // NEW
         BestAsk = t.BestAsk ?? 0        // NEW
     };
     ```

2. **TickerData.cs** (Domain Model)
   - Add properties:
     ```csharp
     public decimal BestBid { get; set; }
     public decimal BestAsk { get; set; }
     ```

3. **TradeAggregatorService.cs** (Calculation Logic)
   - Add `CalculateSpread()` method:
     ```csharp
     private (decimal spreadPercent, decimal spreadAbsolute) CalculateSpread(decimal bid, decimal ask)
     {
         if (ask <= 0 || bid <= 0) return (0, 0);

         var spreadAbs = ask - bid;
         var spreadPct = (spreadAbs / ask) * 100;

         return (spreadPct, spreadAbs);
     }
     ```
   - Call during aggregation:
     ```csharp
     var ticker = _latestTickers.GetValueOrDefault(symbolKey);
     if (ticker != null)
     {
         var (spreadPct, spreadAbs) = CalculateSpread(ticker.BestBid, ticker.BestAsk);
         metadata.SpreadPercent = spreadPct;
         metadata.SpreadAbsolute = spreadAbs;
     }
     ```

4. **SymbolMetadata.cs** (Add Fields)
   - Add properties:
     ```csharp
     public decimal SpreadPercent { get; set; }
     public decimal SpreadAbsolute { get; set; }
     ```

5. **WebSocket Broadcast**
   - Include spread in `all_symbols_scored` message:
     ```csharp
     spreadPercent = m.SpreadPercent,
     spreadAbsolute = m.SpreadAbsolute
     ```

### **Frontend Changes**

6. **index.html & table.html** (Table View)
   - Add column header:
     ```html
     <th class="sortable" data-column="spread">Spread %</th>
     ```
   - Add data mapping:
     ```javascript
     spread: s.spreadPercent || 0
     ```
   - Add table cell with color coding:
     ```html
     <td class="spread ${s.spread < 0.1 ? 'tight' : s.spread > 0.5 ? 'wide' : ''}">
         ${s.spread.toFixed(3)}%
     </td>
     ```

7. **screener.js** (Charts View)
   - Add spread to card stats:
     ```javascript
     statsEl.textContent = `${trades5m}/5m | Spread: ${spread.toFixed(3)}%`;
     ```
   - Optional: Highlight cards with tight spreads (< 0.1%)

8. **CSS Styling**
   - Add spread color classes:
     ```css
     .spread {
         font-family: 'JetBrains Mono', monospace;
     }
     .spread.tight {
         color: var(--success);  /* Green for tight spreads */
     }
     .spread.wide {
         color: var(--danger);   /* Red for wide spreads */
     }
     ```

---

## 📐 Technical Details

**Data Source Options:**

1. **Ticker API (Recommended):**
   - Already fetched every 10 seconds
   - Contains `BestBid` and `BestAsk` fields
   - ✅ No additional API calls needed

2. **Orderbook WebSocket (Alternative):**
   - Real-time updates
   - More accurate but higher complexity
   - ❌ Requires new WebSocket subscriptions

**Edge Cases:**
- Missing bid/ask data → Return `0` or hide column
- Bid > Ask (data error) → Return `0` and log warning
- Zero prices → Skip calculation

**Performance:**
- Spread calculation is O(1) per symbol
- No historical data required
- Minimal CPU overhead

---

## ✅ Success Criteria

1. ✅ Spread calculated correctly from ticker bid/ask prices
2. ✅ Spread column appears in both Table and Charts views
3. ✅ Spread values are sortable in table view
4. ✅ Color-coded: Green (< 0.1%), Yellow (0.1-0.5%), Red (> 0.5%)
5. ✅ No API rate limit issues

---

## 🎓 Testing Plan

**Manual Test:**
1. Start app and wait for first ticker refresh
2. Check table: Spread column shows percentages (e.g., 0.05%)
3. Sort by Spread ascending → Tightest spreads on top
4. Verify color coding matches thresholds

**Expected Values:**
- High liquidity pairs (BTC, ETH): 0.01-0.05%
- Medium liquidity: 0.1-0.3%
- Low liquidity (new coins): 0.5-2%

**Validation:**
```
Manual check: Open MEXC website, compare spread values
```

---

## 📝 Notes

- Spread is a **real-time indicator** (updates every 10s with ticker refresh)
- Tight spreads can widen suddenly during volatility spikes
- Consider adding "spread spike" detection (when spread > 2x average)

---

## 🔗 Related Sprints

- **SPRINT-11:** NATR indicator (volatility)
- **SPRINT-13:** Volume spike detection
- **SPRINT-14:** Large prints tracking

---

**Next Steps:** Extract bid/ask from MEXC ticker API and calculate spread percentage
