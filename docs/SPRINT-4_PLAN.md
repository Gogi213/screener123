# SPRINT-4: WebSocket Resilience

**Priority:** 🔴 CRITICAL  
**Estimated Time:** 15 minutes  
**Status:** Planned

---

## 🎯 Goal

Add automatic WebSocket reconnection with exponential backoff.

**Problem:** Client зависает при server restart или network disconnect.  
**Solution:** Auto-reconnect with user-friendly status messages.

---

## 📋 Tasks

### Task 4.1: WebSocket Reconnection Logic (10 min)

**File:** `src/SpreadAggregator.Presentation/wwwroot/js/screener.js`

**Changes:**
1. Wrap WebSocket connection в reconnection loop
2. Exponential backoff: 1s, 2s, 4s, 8s, max 30s
3. Status indicator: "Connecting...", "Connected ✅", "Reconnecting ⚠️"
4. Auto-reconnect on disconnect

**Implementation:**
```javascript
let reconnectAttempt = 0;
const maxReconnectDelay = 30000; // 30 sec

function connectWebSocket() {
    const ws = new WebSocket('ws://localhost:8181');
    
    ws.onopen = () => {
        console.log('WebSocket connected');
        reconnectAttempt = 0;
        updateStatus('Connected ✅', 'green');
    };
    
    ws.onclose = () => {
        console.log('WebSocket closed, reconnecting...');
        updateStatus('Reconnecting ⚠️', 'orange');
        scheduleReconnect();
    };
    
    ws.onerror = (error) => {
        console.error('WebSocket error:', error);
    };
    
    ws.onmessage = (event) => {
        // Existing message handling
    };
    
    return ws;
}

function scheduleReconnect() {
    const delay = Math.min(1000 * Math.pow(2, reconnectAttempt), maxReconnectDelay);
    reconnectAttempt++;
    
    console.log(`Reconnecting in ${delay}ms (attempt ${reconnectAttempt})...`);
    setTimeout(() => {
        globalWebSocket = connectWebSocket();
    }, delay);
}

function updateStatus(message, color) {
    const statusEl = document.getElementById('connection-status');
    if (statusEl) {
        statusEl.textContent = message;
        statusEl.style.color = color;
    }
}
```

**Testing:**
1. Start server
2. Load page → "Connected ✅"
3. Stop server → "Reconnecting ⚠️"
4. Start server → auto-reconnect → "Connected ✅"

---

### Task 4.2: Connection Status UI (5 min)

**File:** `src/SpreadAggregator.Presentation/wwwroot/index.html`

**Changes:**
Add connection status indicator to header

```html
<div class="header">
    <h1>MEXC Trade Screener</h1>
    <div id="connection-status" style="color: gray;">Connecting...</div>
</div>
```

**CSS:** (optional styling)
```css
#connection-status {
    font-size: 14px;
    font-weight: bold;
    margin-left: 20px;
}
```

---

## ✅ Acceptance Criteria

- [ ] WebSocket auto-reconnects after disconnect
- [ ] Exponential backoff prevents spam
- [ ] User sees connection status
- [ ] Page usable after server restart (no manual reload needed)

---

## 🎓 Why This Sprint?

**GEMINI_DEV Validation:**
- ✅ Measured problem (restart → client dead)
- ✅ Minimal complexity (one function)
- ✅ High impact (system useless without this)
- ✅ No new dependencies

**Filosofiya:** Working resilience > Perfect architecture
