# SPRINT-5: Connection Health Monitoring

**Priority:** 🟡 MEDIUM  
**Estimated Time:** 10 minutes  
**Status:** Planned

---

## 🎯 Goal

Add browser notification when MEXC connection is stale (Events/sec = 0).

**Problem:** MEXC disconnect silent - user не знает что data stale.  
**Solution:** Visual alert when no trades for 30+ seconds.

---

## 📋 Tasks

### Task 5.1: Events/sec Tracking (5 min)

**File:** `src/SpreadAggregator.Presentation/wwwroot/js/screener.js`

**Changes:**
Track last trade timestamp, alert if stale

```javascript
let lastTradeTimestamp = Date.now();
let healthCheckInterval = null;

// In WebSocket onmessage handler:
ws.onmessage = (event) => {
    try {
        const msg = JSON.parse(event.data);
        
        if (msg.type === 'trade_update') {
            lastTradeTimestamp = Date.now(); // Update on every trade
        }
        
        // ... existing handling
    } catch (error) {
        console.error('WebSocket message error:', error);
    }
};

// Start health monitoring
function startHealthCheck() {
    healthCheckInterval = setInterval(() => {
        const timeSinceLastTrade = Date.now() - lastTradeTimestamp;
        
        if (timeSinceLastTrade > 30000) { // 30 seconds
            showHealthAlert('⚠️ No trades for 30+ seconds. MEXC connection may be down.');
        } else {
            hideHealthAlert();
        }
    }, 5000); // Check every 5 seconds
}

function showHealthAlert(message) {
    const alertEl = document.getElementById('health-alert');
    if (alertEl) {
        alertEl.textContent = message;
        alertEl.style.display = 'block';
    }
}

function hideHealthAlert() {
    const alertEl = document.getElementById('health-alert');
    if (alertEl) {
        alertEl.style.display = 'none';
    }
}
```

---

### Task 5.2: Health Alert UI (5 min)

**File:** `src/SpreadAggregator.Presentation/wwwroot/index.html`

**Changes:**
Add alert banner

```html
<div id="health-alert" class="health-alert" style="display: none;">
    ⚠️ No trades detected
</div>
```

**CSS:**
```css
.health-alert {
    position: fixed;
    top: 60px;
    left: 50%;
    transform: translateX(-50%);
    background: #ff9800;
    color: white;
    padding: 12px 24px;
    border-radius: 8px;
    font-weight: bold;
    z-index: 9999;
    box-shadow: 0 4px 12px rgba(0,0,0,0.3);
}
```

---

## ✅ Acceptance Criteria

- [ ] Alert shows when no trades for 30+ sec
- [ ] Alert disappears when trades resume
- [ ] Non-intrusive (banner, not blocking modal)
- [ ] User aware of stale data

---

## 🎓 Why This Sprint?

**GEMINI_DEV Validation:**
- ✅ User experience improvement
- ✅ Simple implementation (setTimeout)
- ✅ No new dependencies
- ✅ Fail-fast visibility (user knows problem exists)

**Философия:** Observable problems > Silent failures
