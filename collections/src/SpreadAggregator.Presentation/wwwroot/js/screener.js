// CONFIG
const HISTORY_MINUTES = 30;

// BLACKLIST (Pairs to hide)
const BLACKLIST = [
    'BTCUSDT', 'ETHUSDT', 'BTCUSDE', 'USDCUSDT', 'ETHUSDC',
    'BNBUSDT', 'DOGEUSDT', 'SOLUSDT', 'XRPUSDC', 'SUIUSDT', 'DOGEUSDE',
    'FDUSDUSDT', 'CRVUSDC'
].map(s => s.toUpperCase());

// STATE
let allSymbols = [];
const activeCharts = new Map();     // Symbol -> uPlot Instance
let isFirstLoad = true;              // ANTI-FLICKER: Track if this is the first data load

// uPlot DATA STRUCTURE - Map<symbol, {times: [], buys: [], sells: [], startIndex: number}>
const chartData = new Map();

// SMART SORTING STATE
let smartSortEnabled = true;
const symbolActivity = new Map(); // Symbol -> { trades3m: number, lastUpdate: timestamp }
let smartSortInterval = null;

// BATCHING STATE
const pendingChartUpdates = new Map(); // symbol -> [trades]

// HEALTH MONITORING STATE (SPRINT-5)
let lastTradeTimestamp = Date.now();
let healthCheckInterval = null;

// WEBSOCKET RECONNECTION STATE (SPRINT-4)
let reconnectAttempt = 0;
const MAX_RECONNECT_DELAY = 30000; // 30 seconds

// DOM ELEMENTS
const grid = document.getElementById('grid');
const statusText = document.getElementById('status-text');

// GLOBAL WebSocket connection
let globalWebSocket = null;

// CORE LOGIC
async function init() {
    try {
        statusText.textContent = "Connecting to WebSocket...";

        // Connect WebSocket - it will send all symbols automatically
        initGlobalWebSocket();

    } catch (e) {
        console.error("Init error:", e);
        statusText.textContent = "Connection Error";
        setTimeout(init, 5000);
    }
}

function cleanupPage() {
    // Destroy uPlot instances properly (UI cleanup only)
    activeCharts.forEach(uplot => {
        uplot.destroy();
    });
    activeCharts.clear();
    // NOTE: chartData is NOT cleared - data persists for all symbols
    grid.innerHTML = '';
}

function renderPage(autoScroll = false) {
    // Save current scroll position before cleanup
    const savedScrollY = window.scrollY;

    cleanupPage();

    // Render TOP-30 only (prevent browser crash from too many charts)
    const top30 = allSymbols.slice(0, 30);
    statusText.textContent = `Live: TOP-30 of ${allSymbols.length} Pairs (sorted by trades/3m)`;

    // Render only top 30 symbols
    top30.forEach(s => createCard(s.symbol, s.tradeCount));

    // Handle scroll: either restore saved position or scroll to top
    if (autoScroll) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    } else {
        // Restore scroll position (for smart sort - don't jump)
        window.scrollTo({ top: savedScrollY, behavior: 'instant' });
    }
}



function createCard(symbol, initialTradeCount) {
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = `
        <div class="card-header">
            <div class="symbol-name" data-symbol="${symbol}">${symbol}</div>
            <div class="trade-stats">
                <span id="stats-${symbol}">0/3m</span>
                <span id="accel-${symbol}" class="acceleration" style="margin-left: 8px; color: #f59e0b; font-size: 0.85em;"></span>
            </div>
        </div>
        <div class="price-info" id="price-${symbol}">
            <span class="price-val">---</span>
        </div>
        <div class="chart-container" id="chart-${symbol}"></div>
    `;
    grid.appendChild(card);

    // Click-to-copy
    const symbolEl = card.querySelector('.symbol-name');
    symbolEl.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(symbol);
            const originalColor = symbolEl.style.color;
            symbolEl.style.color = '#10b981';
            setTimeout(() => { symbolEl.style.color = originalColor; }, 200);
        } catch (err) { console.error('Failed to copy:', err); }
    });

    // --- uPLOT INITIALIZATION ---
    const container = document.getElementById(`chart-${symbol}`);

    const opts = {
        width: container.offsetWidth || 400,
        height: container.offsetHeight || 200,
        cursor: { show: false },
        legend: { show: false },
        scales: {
            x: { time: false },
            y: { auto: true }
        },
        series: [
            { label: 'Time' },
            {
                label: 'Buy',
                // RENDER OPTIMIZATION: No stroke/fill, only scatter points
                stroke: 'transparent',
                width: 0,
                points: {
                    show: true,
                    size: 3,  // Balanced size for readability
                    stroke: '#10b981',
                    fill: '#10b981'
                }
            },
            {
                label: 'Sell',
                // RENDER OPTIMIZATION: No stroke/fill, only scatter points
                stroke: 'transparent',
                width: 0,
                points: {
                    show: true,
                    size: 3,  // Balanced size for readability
                    stroke: '#ef4444',
                    fill: '#ef4444'
                }
            }
        ],
        axes: [
            { show: false }, // X axis (time) - hidden
            {
                show: true,
                side: 1,
                stroke: '#6b7280',
                grid: {
                    show: true,
                    stroke: '#374151',
                    width: 1
                },
                ticks: {
                    show: true,
                    stroke: '#6b7280',
                    width: 1
                }
            }
        ]
    };

    // Initialize data storage if not exists (preserve data across page changes)
    if (!chartData.has(symbol)) {
        chartData.set(symbol, {
            times: [],
            buys: [],
            sells: [],
            startIndex: 0
        });
    }

    // Load existing data into uPlot (may be empty or accumulated from previous pages)
    const symbolData = chartData.get(symbol);
    const data = [
        symbolData.times.slice(symbolData.startIndex),
        symbolData.buys.slice(symbolData.startIndex),
        symbolData.sells.slice(symbolData.startIndex)
    ];
    const uplot = new uPlot(opts, data, container);

    activeCharts.set(symbol, uplot);

    // Initialize stats from existing data (if any) to prevent "0/3m" and "---"
    if (symbolData.times.length > 0) {
        // Find last valid price (check both buys and sells)
        let lastPrice = null;
        for (let i = symbolData.times.length - 1; i >= symbolData.startIndex; i--) {
            if (symbolData.buys[i] !== null) {
                lastPrice = symbolData.buys[i];
                break;
            }
            if (symbolData.sells[i] !== null) {
                lastPrice = symbolData.sells[i];
                break;
            }
        }

        // Update stats with existing data
        if (lastPrice !== null) {
            updateCardStats(symbol, lastPrice);
        }
    }
}

// --- BATCHING LOOP (requestAnimationFrame with 1000ms throttle) ---
let lastBatchUpdate = 0;

function batchingLoop() {
    const now = performance.now();

    // RENDER OPTIMIZATION: Throttle 1000ms (1 second)
    if (now - lastBatchUpdate > 1000 && pendingChartUpdates.size > 0) {
        pendingChartUpdates.forEach((trades, symbol) => {
            if (trades.length === 0) return;

            // ALWAYS update chartData for ALL symbols (even not visible on current page)
            trades.forEach(t => addTradeToChart(symbol, t));

            // Only update UI for visible charts
            const uplot = activeCharts.get(symbol);
            const data = chartData.get(symbol);

            if (uplot && data) {
                // Update stats
                const lastTrade = trades[trades.length - 1];
                updateCardStats(symbol, lastTrade.price);

                // Update uPlot with active data (circular buffer - skip deleted indices)
                uplot.setData([
                    data.times.slice(data.startIndex),
                    data.buys.slice(data.startIndex),
                    data.sells.slice(data.startIndex)
                ]);
            }
        });

        pendingChartUpdates.clear();
        lastBatchUpdate = now;
    }

    requestAnimationFrame(batchingLoop);
}

// Start the loop
batchingLoop();

// ===================================================================
// WEBSOCKET CONNECTION
// ===================================================================
function initGlobalWebSocket() {
    if (globalWebSocket && globalWebSocket.readyState === WebSocket.OPEN) return;

    globalWebSocket = new WebSocket(`ws://${window.location.hostname}:8181`);

    globalWebSocket.onopen = () => {
        console.log('[WebSocket] Connected to broadcast server');

        // SPRINT-4: Reset reconnection counter on successful connect
        reconnectAttempt = 0;

        // SPRINT-5 integration: Reset health timestamp to prevent false alerts
        lastTradeTimestamp = Date.now();

        statusText.textContent = `Live: ${allSymbols.length} Pairs`;
    };

    globalWebSocket.onmessage = (event) => {
        try {
            const msg = JSON.parse(event.data);

            // SPRINT-9: Handle OHLCV aggregates (200ms timeframe)
            if (msg.type === 'trade_aggregate' && msg.symbol && msg.aggregate) {
                const symbol = msg.symbol.replace('MEXC_', '');

                if (!chartData.has(symbol)) {
                    chartData.set(symbol, {
                        times: [],
                        buys: [],
                        sells: [],
                        startIndex: 0
                    });
                }

                lastTradeTimestamp = Date.now();

                // Convert aggregate to pseudo-trade (use close price, side from volume ratio)
                const agg = msg.aggregate;
                const pseudoTrade = {
                    price: agg.close,
                    quantity: agg.volume / agg.close,
                    side: agg.buyVolume > agg.sellVolume ? 'Buy' : 'Sell',
                    timestamp: agg.timestamp
                };

                const pending = pendingChartUpdates.get(symbol) || [];
                pending.push(pseudoTrade);
                pendingChartUpdates.set(symbol, pending);
            }
            else if (msg.type === 'trade_update' && msg.symbol && msg.trades) {
                // Handle individual trade updates for charts
                const symbol = msg.symbol.replace('MEXC_', '');

                // SPRINT-6: Pre-initialize chartData to prevent data loss on page reload
                // Without this, trades received before all_symbols_scored (0-2 sec) are lost
                if (!chartData.has(symbol)) {
                    chartData.set(symbol, {
                        times: [],
                        buys: [],
                        sells: [],
                        startIndex: 0
                    });
                }

                // SPRINT-5: Update health timestamp
                lastTradeTimestamp = Date.now();

                // Accumulate trades for ALL symbols (even not visible on current page)
                const pending = pendingChartUpdates.get(symbol) || [];
                pending.push(...msg.trades);
                pendingChartUpdates.set(symbol, pending);
            }
            else if (msg.type === 'all_symbols_scored') {
                // SPRINT-3: Server now sorts by trades3m, receive and store it
                // FILTER: Remove blacklisted pairs
                allSymbols = msg.symbols
                    .filter(s => !BLACKLIST.includes(s.symbol.toUpperCase()))
                    .map(s => {
                        // Update activity map with trades3m AND acceleration from server
                        symbolActivity.set(s.symbol, {
                            trades3m: s.trades3m || 0,
                            acceleration: s.acceleration || 1.0,
                            lastUpdate: Date.now()
                        });

                        return {
                            symbol: s.symbol,
                            score: s.score,
                            tradesPerMin: s.tradesPerMin,
                            trades3m: s.trades3m || 0,
                            acceleration: s.acceleration || 1.0,
                            lastPrice: s.lastPrice,
                            lastUpdate: s.lastUpdate
                        };
                    });

                // ANTI-FLICKER FIX: Only render page on first load
                // After that, Smart Sort will handle re-rendering if enabled
                if (isFirstLoad) {
                    renderPage();
                    isFirstLoad = false;
                    console.log('[Screener] Initial render complete. Flicker protection enabled.');
                }
                // Data is updated in allSymbols, but no re-render unless Smart Sort triggers it
            }
        } catch (error) {
            console.error('[WebSocket] Parse error:', error);
        }
    };

    globalWebSocket.onclose = () => {
        console.log('[WebSocket] Disconnected');

        // SPRINT-4: Exponential backoff reconnection
        const delay = Math.min(1000 * Math.pow(2, reconnectAttempt), MAX_RECONNECT_DELAY);
        reconnectAttempt++;

        console.log(`[WebSocket] Reconnecting in ${delay}ms (attempt ${reconnectAttempt})...`);
        statusText.textContent = "Reconnecting...";

        setTimeout(initGlobalWebSocket, delay);
    };
}

function addTradeToChart(symbol, trade) {
    const data = chartData.get(symbol);
    if (!data) return;

    // Parse timestamp (ms)
    let timestamp = typeof trade.timestamp === 'string'
        ? new Date(trade.timestamp).getTime()
        : trade.timestamp;

    // Check if timestamp is seconds (small number)
    if (timestamp < 10000000000) timestamp *= 1000;

    const price = parseFloat(trade.price);
    const isBuy = (trade.side === "Buy" || trade.side === 0 || trade.side === "buy");

    if (isNaN(price)) return;

    // Add to synchronized arrays (uPlot format)
    data.times.push(timestamp);
    data.buys.push(isBuy ? price : null);
    data.sells.push(isBuy ? null : price);

    // Circular buffer: increment startIndex instead of shift() - O(1) vs O(n)
    const activeLength = data.times.length - data.startIndex;
    if (activeLength > 2000) {
        data.startIndex += 100; // Mark first 100 as "deleted"
    }

    // Periodically compact arrays to free memory
    if (data.startIndex > 500) {
        data.times = data.times.slice(data.startIndex);
        data.buys = data.buys.slice(data.startIndex);
        data.sells = data.sells.slice(data.startIndex);
        data.startIndex = 0;
    }
}

function updateCardStats(symbol, price) {
    const data = chartData.get(symbol);
    if (!data) return;

    // SPRINT-3: Calculate Trades/3Min (changed from 1 minute)
    const threeMinutesAgo = Date.now() - 180000; // 3 minutes in ms
    let count = 0;

    // Count from times array (iterate backwards)
    for (let i = data.times.length - 1; i >= 0; i--) {
        if (data.times[i] < threeMinutesAgo) break;
        // Count non-null values in buys or sells
        if (data.buys[i] !== null || data.sells[i] !== null) {
            count++;
        }
    }

    // 2. Update UI
    const priceEl = document.getElementById(`price-${symbol}`);
    const statsEl = document.getElementById(`stats-${symbol}`);
    const accelEl = document.getElementById(`accel-${symbol}`);

    if (priceEl) {
        priceEl.innerHTML = `<span class="price-val" style="font-family: 'JetBrains Mono';">${formatTickSize(price)}</span>`;
    }

    if (statsEl) {
        statsEl.textContent = `${count}/3m`;  // SPRINT-3: Display /3m instead of /1m
    }

    // ACCELERATION: ALWAYS SHOW (gray if < 2.0x, colored if >= 2.0x)
    if (accelEl) {
        const activity = symbolActivity.get(symbol);
        const accel = activity?.acceleration || 1.0;

        // Always display
        accelEl.textContent = `↑${accel.toFixed(1)}x`;
        accelEl.style.display = 'inline';

        // Color coding: gray if slow, colored if fast
        if (accel >= 3.0) {
            accelEl.style.color = '#ef4444'; // red - extreme
        } else if (accel >= 2.0) {
            accelEl.style.color = '#f97316'; // orange - high
        } else {
            accelEl.style.color = '#6b7280'; // gray - normal/low
        }
    }

    // NOTE: We DON'T update symbolActivity here anymore!
    // Sorting uses server-provided trades3m data (from all_symbols_scored message)
    // Local 'count' is only for UI display, not for sorting
}

function formatTickSize(tick) {
    if (!tick || tick <= 0) return '---';
    if (tick >= 1) return tick.toFixed(2);
    if (tick >= 0.01) return tick.toFixed(4);
    if (tick >= 0.0001) return tick.toFixed(6);
    return tick.toFixed(8).replace(/\.?0+$/, '');
}

// SMART SORTING
function toggleSmartSort() {
    smartSortEnabled = !smartSortEnabled;
    const btn = document.getElementById('btnSortToggle');
    const icon = document.getElementById('sortIcon');
    btn.classList.toggle('active');

    if (smartSortEnabled) {
        icon.textContent = '🔥';
        btn.innerHTML = '<span id="sortIcon">🔥</span> Live Sort';
        startSmartSort();
    } else {
        icon.textContent = '❄️';
        btn.innerHTML = '<span id="sortIcon">❄️</span> Frozen';
        stopSmartSort();
    }
}

function startSmartSort() {
    if (smartSortInterval) clearInterval(smartSortInterval);
    // ANTI-FLICKER: 10 seconds instead of 2 - less aggressive re-sorting
    smartSortInterval = setInterval(reorderCardsWithoutDestroy, 10000);
}

function stopSmartSort() {
    if (smartSortInterval) {
        clearInterval(smartSortInterval);
        smartSortInterval = null;
    }
}


function reorderCardsWithoutDestroy() {
    if (!smartSortEnabled) return;

    // SPRINT-3: Sort ALL symbols by trades/3m activity (descending)
    allSymbols.sort((a, b) => {
        const actA = symbolActivity.get(a.symbol)?.trades3m || 0;
        const actB = symbolActivity.get(b.symbol)?.trades3m || 0;
        return actB - actA; // Descending - most active first
    });

    // Re-render with sorted data (TOP-30 displayed)
    renderPage();
}

// SPRINT-5: Health Check Functions
function startHealthCheck() {
    if (healthCheckInterval) clearInterval(healthCheckInterval);

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
    let alertEl = document.getElementById('health-alert');
    if (!alertEl) {
        // Create alert element if doesn't exist
        alertEl = document.createElement('div');
        alertEl.id = 'health-alert';
        alertEl.className = 'health-alert';
        document.body.appendChild(alertEl);
    }
    alertEl.textContent = message;
    alertEl.style.display = 'block';
}

function hideHealthAlert() {
    const alertEl = document.getElementById('health-alert');
    if (alertEl) {
        alertEl.style.display = 'none';
    }
}

// Start
init();
startSmartSort();
startHealthCheck(); // SPRINT-5
