# SPRINT-14: Large Prints Tracking ("Прострелы")

**Status:** 📋 Planned
**Created:** 2025-11-26
**Priority:** High
**Complexity:** High

---

## 🎯 Objective

Implement real-time detection and tracking of "прострелы" (large prints) - individual trades that are significantly larger than average and sweep through multiple orderbook levels (2-5% of depth).

---

## 📊 Background

**"Прострел" Definition:**
```
Large Print = Individual trade where:
1. Trade Volume > Average Trade Volume * Threshold (e.g., 5x)
2. Trade sweeps 2-5% of orderbook depth
3. Price impact visible on chart (spike/drop)
```

**Why Track Large Prints?**
- Indicates institutional/whale activity
- Often precedes directional moves (momentum continuation)
- Can signal liquidity exhaustion (reversal points)
- Critical for HFT/scalping to ride the momentum

**Detection Window:** Real-time (per trade update)

---

## 🏗️ Implementation Plan

### **Backend Changes**

1. **TradeAggregatorService.cs** (Detection Logic)

   - Add large print history tracking:
     ```csharp
     private readonly ConcurrentDictionary<string, Queue<LargePrint>> _largePrints = new();
     ```

   - Add `LargePrint` class:
     ```csharp
     public class LargePrint
     {
         public DateTime Timestamp { get; set; }
         public decimal Price { get; set; }
         public decimal Quantity { get; set; }
         public decimal VolumeUSD { get; set; }
         public string Side { get; set; }  // "Buy" or "Sell"
         public decimal AvgTradeVolume { get; set; }
         public decimal Ratio { get; set; }  // VolumeUSD / AvgTradeVolume
     }
     ```

   - Add detection method:
     ```csharp
     private void DetectLargePrint(TradeData trade)
     {
         var symbolKey = $"{trade.Exchange}-{trade.Symbol}";

         // Calculate average trade volume (1-minute rolling window)
         var oneMinAgo = DateTime.UtcNow.AddMinutes(-1);
         if (!_symbolTrades.TryGetValue(symbolKey, out var trades))
             return;

         var recentTrades = trades.Where(t => t.Timestamp >= oneMinAgo).ToList();
         if (recentTrades.Count < 10) return;  // Need baseline

         var avgVolume = recentTrades.Average(t => t.Price * t.Quantity);
         var currentVolume = trade.Price * trade.Quantity;

         // Threshold: 5x average trade volume
         const decimal THRESHOLD = 5.0m;
         var ratio = avgVolume > 0 ? currentVolume / avgVolume : 0;

         if (ratio >= THRESHOLD)
         {
             // Detected large print
             var largePrint = new LargePrint
             {
                 Timestamp = trade.Timestamp,
                 Price = trade.Price,
                 Quantity = trade.Quantity,
                 VolumeUSD = currentVolume,
                 Side = trade.Side,
                 AvgTradeVolume = avgVolume,
                 Ratio = ratio
             };

             // Store in history (keep last 10 large prints)
             if (!_largePrints.TryGetValue(symbolKey, out var history))
             {
                 history = new Queue<LargePrint>();
                 _largePrints[symbolKey] = history;
             }
             history.Enqueue(largePrint);
             if (history.Count > 10) history.Dequeue();

             // Broadcast large print event
             BroadcastLargePrint(symbolKey, largePrint);
         }
     }
     ```

   - Call detection on every trade:
     ```csharp
     private void OnTradeReceived(TradeData trade)
     {
         // Existing aggregation logic...

         // NEW: Detect large prints
         DetectLargePrint(trade);
     }
     ```

2. **SymbolMetadata.cs** (Add Fields)
   - Add recent large print summary:
     ```csharp
     public int LargePrintCount5m { get; set; }  // Count in last 5 minutes
     public decimal LastLargePrintRatio { get; set; }  // Most recent ratio
     public DateTime? LastLargePrintTime { get; set; }
     ```

3. **WebSocket Broadcast**

   - Add new message type `large_print`:
     ```csharp
     private void BroadcastLargePrint(string symbolKey, LargePrint print)
     {
         var msg = new
         {
             type = "large_print",
             symbol = symbolKey.Split('-')[1],
             timestamp = print.Timestamp,
             price = print.Price,
             quantity = print.Quantity,
             volumeUSD = print.VolumeUSD,
             side = print.Side,
             ratio = print.Ratio,
             avgVolume = print.AvgTradeVolume
         };

         _webSocketServer.BroadcastAsync(JsonSerializer.Serialize(msg));
     }
     ```

   - Update `all_symbols_scored` to include summary:
     ```csharp
     largePrintCount5m = m.LargePrintCount5m,
     lastLargePrintRatio = m.LastLargePrintRatio
     ```

### **Frontend Changes**

4. **index.html & table.html** (Table View)
   - Add column header:
     ```html
     <th class="sortable" data-column="largePrints">Large Prints</th>
     ```
   - Add data mapping:
     ```javascript
     largePrints: s.largePrintCount5m || 0,
     lastPrintRatio: s.lastLargePrintRatio || 0
     ```
   - Add table cell with indicator:
     ```html
     <td class="large-prints ${s.largePrints > 0 ? 'active' : ''}">
         ${s.largePrints > 0 ? '⚡ ' : ''}${s.largePrints}
         ${s.largePrints > 0 ? `(${s.lastPrintRatio.toFixed(1)}x)` : ''}
     </td>
     ```

5. **screener.js** (Charts View)

   - Add WebSocket handler for `large_print` messages:
     ```javascript
     ws.onmessage = (event) => {
         const msg = JSON.parse(event.data);

         if (msg.type === 'large_print') {
             handleLargePrint(msg);
         }
         // ... existing handlers
     };

     function handleLargePrint(msg) {
         const card = document.querySelector(`[data-symbol="${msg.symbol}"]`);
         if (!card) return;

         // Flash animation
         card.classList.add('large-print-flash');
         setTimeout(() => card.classList.remove('large-print-flash'), 3000);

         // Show alert badge
         const badge = document.createElement('div');
         badge.className = 'large-print-badge';
         badge.innerHTML = `⚡ ${msg.ratio.toFixed(1)}x ${msg.side}`;
         card.querySelector('.card-header').appendChild(badge);

         // Auto-remove after 10s
         setTimeout(() => badge.remove(), 10000);
     }
     ```

   - Add large print overlay on chart:
     ```javascript
     // Mark large print on uPlot chart
     const chartData = window.chartData[msg.symbol];
     if (chartData) {
         chartData.push([
             msg.timestamp / 1000,
             msg.price,
             msg.side === 'Buy' ? 1 : 0,
             msg.ratio  // Size multiplier for point
         ]);
     }
     ```

6. **CSS Styling**
   - Add large print animations:
     ```css
     .large-prints.active {
         color: var(--accent);
         font-weight: 600;
     }

     .large-print-flash {
         animation: flash 1s ease-out 3;
         border: 2px solid var(--accent) !important;
     }

     @keyframes flash {
         0%, 100% {
             box-shadow: 0 0 20px rgba(255, 119, 0, 0.3);
         }
         50% {
             box-shadow: 0 0 40px rgba(255, 119, 0, 0.8);
         }
     }

     .large-print-badge {
         position: absolute;
         top: 10px;
         right: 10px;
         background: var(--accent);
         color: var(--bg-card);
         padding: 4px 8px;
         border-radius: 4px;
         font-size: 11px;
         font-weight: 600;
         z-index: 10;
         animation: slideIn 0.3s ease-out;
     }

     @keyframes slideIn {
         from { transform: translateX(100%); }
         to { transform: translateX(0); }
     }
     ```

---

## 📐 Technical Details

**Detection Algorithm:**
```
1. Calculate 1-minute rolling average trade volume
2. Compare each incoming trade against average
3. If trade volume > (average * 5), classify as large print
4. Store in history queue (max 10 per symbol)
5. Broadcast real-time alert to frontend
```

**Thresholds:**
```
- 5x average = Moderate large print
- 10x average = Strong large print (institutional)
- 20x+ average = Extreme print (whale sweep)
```

**Memory Management:**
- Keep last 10 large prints per symbol
- Auto-remove prints older than 5 minutes from summary count
- Estimated memory: ~400 symbols x 10 prints x 64 bytes = ~250 KB

**Edge Cases:**
- Low liquidity pairs → May trigger false positives (adjust threshold)
- First minute of new symbol → No baseline, skip detection
- Extreme outliers → Cap ratio at 100x to prevent UI overflow

---

## ✅ Success Criteria

1. ✅ Large prints detected accurately (> 5x average trade volume)
2. ✅ Real-time WebSocket alerts sent to frontend
3. ✅ Chart cards flash with animation on large print
4. ✅ Badge shows ratio and side (Buy/Sell)
5. ✅ Table column shows count in last 5 minutes
6. ✅ No false positives on stable markets

---

## 🎓 Testing Plan

**Manual Test:**
1. Start app and monitor high-volume symbols
2. Wait for a large trade (check MEXC orderbook for reference)
3. Verify:
   - Card flashes with animation
   - Badge appears with ratio (e.g., "⚡ 8.3x Buy")
   - Table column updates count
4. Check large print disappears after 10s (badge) and 5m (count)

**Stress Test:**
- Monitor during high volatility (e.g., news events)
- Ensure UI doesn't freeze with multiple simultaneous large prints

**Expected Behavior:**
- Normal markets: 0-2 large prints per symbol per hour
- Volatile markets: 5-10 large prints per symbol per hour
- Extreme events: 20+ large prints per symbol per hour

---

## 📝 Notes

- Large prints are a **leading indicator** (precede directional moves)
- Combine with NATR (volatility) and volume spike for confirmation
- Consider adding:
  - **Large print heatmap** (visualize concentration of prints)
  - **Audio alerts** for extreme prints (> 20x)
  - **Historical large print log** (CSV export for backtesting)

**Future Enhancement:**
- Track orderbook depth to calculate % of liquidity swept
- Classify prints as "aggressive buy" vs "aggressive sell" based on taker side
- Correlate large prints with price impact (measure slippage)

---

## 🔗 Related Sprints

- **SPRINT-11:** NATR indicator (volatility context)
- **SPRINT-12:** Spread tracking (liquidity context)
- **SPRINT-13:** Volume spike detection (activity context)

---

**Next Steps:** Implement large print detection logic in TradeAggregatorService.cs and create WebSocket alert system
