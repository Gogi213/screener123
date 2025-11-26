# SPRINT-13: Volume Spike Detection

**Status:** 📋 Planned
**Created:** 2025-11-26
**Priority:** High
**Complexity:** Medium-High

---

## 🎯 Objective

Implement real-time volume spike detection to identify sudden surges in trading activity that may indicate breakout opportunities or whale activity.

---

## 📊 Background

**Volume Spike Definition:**
```
Volume Spike = (Current 5m Volume) / (Average 5m Volume over 1h) > Threshold
Threshold = 2.0 (200% of normal volume)
```

**Why Volume Spikes?**
- Early indicator of price movement (precedes breakouts)
- Detects whale accumulation/distribution
- Filters out low-activity periods

**Timeframe:**
- Current window: 5 minutes
- Baseline average: 1 hour (12 x 5m windows)

---

## 🏗️ Implementation Plan

### **Backend Changes**

1. **TradeAggregatorService.cs** (Core Logic)
   - Add volume history tracking:
     ```csharp
     private readonly ConcurrentDictionary<string, Queue<decimal>> _volumeHistory = new();
     ```

   - Add `CalculateVolumeSpike(string symbolKey)` method:
     ```csharp
     private decimal CalculateVolumeSpike(string symbolKey)
     {
         if (!_symbolTrades.TryGetValue(symbolKey, out var trades))
             return 0;

         // Get current 5m volume
         var fiveMinAgo = DateTime.UtcNow.AddMinutes(-5);
         var current5mVolume = trades
             .Where(t => t.Timestamp >= fiveMinAgo)
             .Sum(t => t.Price * t.Quantity);

         // Get volume history (12 x 5m windows = 1h)
         if (!_volumeHistory.TryGetValue(symbolKey, out var history))
         {
             history = new Queue<decimal>();
             _volumeHistory[symbolKey] = history;
         }

         // Add current 5m volume to history
         history.Enqueue(current5mVolume);
         if (history.Count > 12) history.Dequeue();  // Keep only 1h

         // Calculate average 5m volume over 1h
         var avg5mVolume = history.Count > 0 ? history.Average() : current5mVolume;

         // Calculate spike ratio
         if (avg5mVolume == 0) return 0;
         var spikeRatio = current5mVolume / avg5mVolume;

         return spikeRatio;
     }
     ```

2. **SymbolMetadata.cs** (Domain Model)
   - Add properties:
     ```csharp
     public decimal VolumeSpikeRatio { get; set; }  // e.g., 2.5 = 250%
     public bool IsVolumeSpiking { get; set; }       // True if ratio > 2.0
     ```

3. **Background Task** (Periodic Cleanup)
   - Add cleanup for old volume history:
     ```csharp
     // Every 5 minutes, rotate volume history
     Task.Run(async () =>
     {
         while (true)
         {
             await Task.Delay(TimeSpan.FromMinutes(5));
             // History auto-rotates in CalculateVolumeSpike
         }
     });
     ```

4. **WebSocket Broadcast**
   - Include volume spike in `all_symbols_scored`:
     ```csharp
     volumeSpikeRatio = m.VolumeSpikeRatio,
     isVolumeSpiking = m.IsVolumeSpiking
     ```

### **Frontend Changes**

5. **index.html & table.html** (Table View)
   - Add column header:
     ```html
     <th class="sortable" data-column="volumeSpike">Vol Spike</th>
     ```
   - Add data mapping:
     ```javascript
     volumeSpike: s.volumeSpikeRatio || 0,
     isSpiking: s.isVolumeSpiking || false
     ```
   - Add table cell with indicator:
     ```html
     <td class="volume-spike ${s.isSpiking ? 'spiking' : ''}">
         ${s.isSpiking ? '🔥 ' : ''}${s.volumeSpike.toFixed(2)}x
     </td>
     ```

6. **screener.js** (Charts View)
   - Add visual indicator for spiking symbols:
     ```javascript
     if (symbolData.isVolumeSpiking) {
         card.classList.add('volume-spike');
         statsEl.textContent = `${trades5m}/5m | 🔥 ${volumeSpike.toFixed(1)}x`;
     }
     ```

   - Add auto-sort by volume spike:
     ```javascript
     if (window.smartSortEnabled) {
         // Prioritize symbols with volume spikes
         sorted.sort((a, b) => {
             if (a.isVolumeSpiking && !b.isVolumeSpiking) return -1;
             if (!a.isVolumeSpiking && b.isVolumeSpiking) return 1;
             return b.volumeSpikeRatio - a.volumeSpikeRatio;
         });
     }
     ```

7. **CSS Styling**
   - Add volume spike styles:
     ```css
     .volume-spike {
         font-family: 'JetBrains Mono', monospace;
     }
     .volume-spike.spiking {
         color: var(--accent);
         font-weight: 600;
         animation: pulse 2s infinite;
     }

     @keyframes pulse {
         0%, 100% { opacity: 1; }
         50% { opacity: 0.7; }
     }

     .card.volume-spike {
         border: 2px solid var(--accent);
         box-shadow: 0 0 20px rgba(255, 119, 0, 0.3);
     }
     ```

---

## 📐 Technical Details

**Calculation Formula:**
```
Spike Ratio = Current 5m Volume / Average 5m Volume (1h baseline)

Thresholds:
- 1.0x = Normal
- 2.0x = Moderate spike (200%)
- 3.0x = Strong spike (300%)
- 5.0x+ = Extreme spike (whale activity)
```

**Data Requirements:**
- Minimum 1 hour of trade history (12 x 5m windows)
- Volume history stored per symbol
- Auto-rotates every 5 minutes

**Memory Optimization:**
- Queue size capped at 12 elements per symbol
- Old data auto-removed (FIFO)
- Estimated memory: ~400 symbols x 12 values x 8 bytes = ~38 KB

**Edge Cases:**
- New symbol (< 1h data) → Use current 5m volume as baseline
- Zero average volume → Skip spike detection
- Cold start → Spikes stabilize after 1 hour

---

## ✅ Success Criteria

1. ✅ Volume spike calculated correctly (current vs 1h average)
2. ✅ Visual indicator (🔥) appears for spikes > 2.0x
3. ✅ Chart cards auto-highlight spiking symbols
4. ✅ Sortable by spike ratio in table view
5. ✅ No memory leaks (queue auto-rotates)

---

## 🎓 Testing Plan

**Manual Test:**
1. Start app, wait 1 hour for baseline to stabilize
2. Identify a symbol with sudden volume increase
3. Verify spike ratio calculation:
   ```
   Manual: Sum 5m volume / (sum of last 12 x 5m volumes / 12)
   ```
4. Check 🔥 indicator appears when ratio > 2.0x
5. Verify chart card border highlights

**Stress Test:**
- Monitor memory usage over 24 hours
- Verify queue doesn't grow unbounded

**Expected Behavior:**
- Most symbols: 0.8x - 1.2x (normal fluctuation)
- Breakout candidates: 2.0x - 5.0x (moderate spike)
- Whale dumps/pumps: > 5.0x (extreme spike)

---

## 📝 Notes

- Volume spike is a **leading indicator** (precedes price movement)
- Best used in combination with NATR (volatility) and spread (liquidity)
- Consider adding audio/desktop notification for extreme spikes (> 5.0x)
- Future enhancement: Track spike duration (sustained vs flash spike)

---

## 🔗 Related Sprints

- **SPRINT-11:** NATR indicator
- **SPRINT-12:** Spread tracking
- **SPRINT-14:** Large prints ("прострелы") tracking

---

**Next Steps:** Implement volume history tracking and spike calculation in TradeAggregatorService.cs
