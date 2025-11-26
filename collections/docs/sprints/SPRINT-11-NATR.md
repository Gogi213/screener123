# SPRINT-11: NATR Indicator (Normalized Average True Range)

**Status:** 📋 Planned
**Created:** 2025-11-26
**Priority:** High
**Complexity:** Medium

---

## 🎯 Objective

Implement NATR (Normalized Average True Range) indicator with 10 periods over 1-minute timeframe to measure market volatility and identify high-activity trading opportunities.

---

## 📊 Background

**NATR Formula:**
```
TR (True Range) = MAX(High - Low, |High - Close[prev]|, |Low - Close[prev]|)
ATR = EMA(TR, 10 periods)
NATR = (ATR / Close) * 100  // Normalized as percentage
```

**Why NATR?**
- Identifies volatility independent of price level
- Higher NATR = More volatile = Better for scalping
- Normalized metric allows comparison across different price ranges

**Timeframe:** 1-minute candles, 10-period EMA

---

## 🏗️ Implementation Plan

### **Backend Changes**

1. **TradeAggregatorService.cs** (Primary Logic)
   - Add `CalculateNATR(string symbolKey)` method
   - Use existing `_symbolTrades` queue to generate 1m candles
   - Calculate TR for each 1m candle:
     ```csharp
     var high = candle.Max(t => t.Price);
     var low = candle.Min(t => t.Price);
     var close = candle.Last().Price;
     var prevClose = previousCandle?.Last().Price ?? close;

     var tr = Math.Max(
         high - low,
         Math.Max(
             Math.Abs(high - prevClose),
             Math.Abs(low - prevClose)
         )
     );
     ```
   - Calculate 10-period EMA of TR (ATR)
   - Normalize: `NATR = (ATR / Close) * 100`

2. **SymbolMetadata.cs** (Domain Model)
   - Add property:
     ```csharp
     public decimal NATR { get; set; }
     ```

3. **WebSocket Broadcast**
   - Include `natr` field in `all_symbols_scored` message:
     ```csharp
     natr = m.NATR
     ```

### **Frontend Changes**

4. **index.html & table.html** (Table View)
   - Add column header:
     ```html
     <th class="sortable" data-column="natr">NATR %</th>
     ```
   - Add data mapping:
     ```javascript
     natr: s.natr || 0
     ```
   - Add table cell:
     ```html
     <td class="natr">${s.natr.toFixed(2)}%</td>
     ```

5. **screener.js** (Charts View)
   - Add NATR to card stats:
     ```javascript
     statsEl.textContent = `${trades5m}/5m | NATR: ${natr.toFixed(2)}%`;
     ```
   - Optional: Color-code cards by NATR level (green = high volatility)

6. **CSS Styling**
   - Add `.natr` class with accent color for high values:
     ```css
     .natr {
         color: var(--text-secondary);
         font-family: 'JetBrains Mono', monospace;
     }
     .natr.high { color: var(--accent); }  /* NATR > 5% */
     ```

---

## 📐 Technical Details

**Data Requirements:**
- Minimum 10 x 1-minute candles = 10 minutes of trade history
- Use `_symbolTrades` queue (already tracks trades with timestamps)
- Group trades into 1-minute buckets using `Timestamp`

**EMA Calculation:**
```csharp
// Exponential Moving Average (10 periods)
decimal multiplier = 2.0m / (10 + 1);
decimal ema = tr;  // First value
foreach (var nextTr in trValues.Skip(1))
{
    ema = ((nextTr - ema) * multiplier) + ema;
}
```

**Edge Cases:**
- Symbol with < 10 minutes of data → Return `0` or skip NATR
- Zero close price → Return `0` to avoid division by zero
- Cold start → NATR stabilizes after 10 minutes

---

## ✅ Success Criteria

1. ✅ NATR calculated correctly using 10-period EMA on 1m candles
2. ✅ NATR column appears in both Table and Charts views
3. ✅ NATR values are sortable in table view
4. ✅ Charts can be sorted by NATR (highest volatility first)
5. ✅ No performance degradation (target < 5ms per symbol)

---

## 🎓 Testing Plan

**Manual Test:**
1. Start app, wait 10 minutes for data accumulation
2. Check table: NATR column shows percentages (e.g., 3.45%)
3. Sort by NATR descending → High volatility symbols on top
4. Switch to Charts view → Cards show NATR in stats

**Expected Values:**
- Low volatility (stablecoins): NATR ~ 0.1-0.5%
- Medium volatility (altcoins): NATR ~ 1-3%
- High volatility (new listings): NATR > 5%

---

## 📝 Notes

- NATR is a **lagging indicator** (uses 10 periods of historical data)
- Best used in combination with other metrics (trades/5m, volume spike)
- Consider adding NATR threshold filter (e.g., only show symbols with NATR > 2%)

---

## 🔗 Related Sprints

- **SPRINT-12:** Spread (bid-ask) tracking
- **SPRINT-13:** Volume spike detection
- **SPRINT-14:** Large prints ("прострелы") tracking

---

**Next Steps:** Implement backend NATR calculation in TradeAggregatorService.cs
