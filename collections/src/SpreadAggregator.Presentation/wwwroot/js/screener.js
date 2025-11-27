// CONFIG
const HISTORY_MINUTES = 30;

// BLACKLIST (Pairs to hide)
const BLACKLIST = [
    'BTCUSDT', 'ETHUSDT', 'BTCUSDE', 'USDCUSDT', 'ETHUSDC',
    'BNBUSDT', 'DOGEUSDT', 'SOLUSDT', 'XRPUSDC', 'SUIUSDT', 'DOGEUSDE',
    'FDUSDUSDT', 'CRVUSDC'
].map(s => s.toUpperCase());

// STATE - Use window object directly to avoid "already declared" errors
// When loaded via index.html, variables are already declared there
// When loaded standalone, we initialize them here
window.allSymbols = window.allSymbols || [];
window.isFirstLoad = window.isFirstLoad !== undefined ? window.isFirstLoad : true;
window.smartSortEnabled = window.smartSortEnabled !== undefined ? window.smartSortEnabled : true;
window.smartSortInterval = window.smartSortInterval || null;
window.lastTradeTimestamp = window.lastTradeTimestamp || Date.now();
window.healthCheckInterval = window.healthCheckInterval || null;
window.reconnectAttempt = window.reconnectAttempt || 0;
window.globalWebSocket = window.globalWebSocket || null;

// Local state (not shared)
const activeCharts = new Map();     // Symbol -> uPlot Instance
const chartData = new Map();         // uPlot DATA STRUCTURE
const symbolActivity = new Map();    // Symbol -> { trades5m: number, lastUpdate: timestamp }
const pendingChartUpdates = new Map(); // symbol -> [trades]

// CONSTANTS
const MAX_RECONNECT_DELAY = 30000; // 30 seconds

// DOM ELEMENTS
const grid = document.getElementById('grid');
const statusText = document.getElementById('status-text'); // May be null in tab mode
const isEmbeddedMode = !statusText; // Detect if running in unified tab mode

// CORE LOGIC
async function init() {
    try {
        // In embedded mode, WebSocket is managed by parent (index.html)
        if (isEmbeddedMode) {
            console.log('[Screener] Running in embedded tab mode');
            // WebSocket handled by parent, just wait for data
            return;
        }

        // Standalone mode: manage own WebSocket
        if (statusText) statusText.textContent = "Connecting to WebSocket...";
        initGlobalWebSocket();

    } catch (e) {
        console.error("Init error:", e);
        if (statusText) statusText.textContent = "Connection Error";
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
    const top30 = window.allSymbols.slice(0, 30);
    if (statusText) {
        statusText.textContent = `Live: TOP-30 of ${window.allSymbols.length} Pairs (sorted by trades/5m)`;
    }

    // Render only top 30 symbols
    top30.forEach(s => createCard(s.symbol, s.trades5m || 0));

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
                <span id="stats-${symbol}">0/5m</span>
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

    // Initialize stats immediately with server data
    const serverSymbol = window.allSymbols.find(s => s.symbol === symbol);

    // Set initial trades count from server (always available)
    const statsEl = document.getElementById(`stats-${symbol}`);
    if (statsEl && serverSymbol) {
        statsEl.textContent = `${serverSymbol.trades5m || 0}/5m`;
    }

    // Initialize price from chart data (if any) to prevent "---"
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
    if (window.globalWebSocket && window.globalWebSocket.readyState === WebSocket.OPEN) return;

    window.globalWebSocket = new WebSocket(`ws://${window.location.hostname}:8181`);

    window.globalWebSocket.onopen = () => {
        console.log('[WebSocket] Connected to broadcast server');

        // SPRINT-4: Reset reconnection counter on successful connect
        window.reconnectAttempt = 0;

        // SPRINT-5 integration: Reset health timestamp to prevent false alerts
        window.lastTradeTimestamp = Date.now();

        if (statusText) {
            statusText.textContent = `Live: ${window.allSymbols.length} Pairs`;
        }
    };

    window.globalWebSocket.onmessage = (event) => {
        try {
            const msg = JSON.parse(event.data);

            // SPRINT-14: Handle large print events (прострелы)
            if (msg.type === 'large_print' && msg.symbol) {
                handleLargePrint(msg);
            }
            // Handle price breakthrough events
            if (msg.type === 'price_breakthrough' && msg.symbol) {
                handlePriceBreakthrough(msg);
            }
            // SPRINT-9: Handle OHLCV aggregates (200ms timeframe)
            else if (msg.type === 'trade_aggregate' && msg.symbol && msg.aggregate) {
                const symbol = msg.symbol.replace('MEXC_', '');

                if (!chartData.has(symbol)) {
                    chartData.set(symbol, {
                        times: [],
                        buys: [],
                        sells: [],
                        startIndex: 0
                    });
                }

                window.lastTradeTimestamp = Date.now();

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
                window.lastTradeTimestamp = Date.now();

                // Accumulate trades for ALL symbols (even not visible on current page)
                const pending = pendingChartUpdates.get(symbol) || [];
                pending.push(...msg.trades);
                pendingChartUpdates.set(symbol, pending);
            }
            else if (msg.type === 'all_symbols_scored') {
                // Server sends trades5m, receive and store it
                // FILTER: Remove blacklisted pairs
                window.allSymbols = msg.symbols
                    .filter(s => !BLACKLIST.includes(s.symbol.toUpperCase()))
                    .map(s => {
                        // Update activity map with trades5m AND acceleration from server
                        symbolActivity.set(s.symbol, {
                            trades5m: s.trades5m || 0,
                            acceleration: s.acceleration || 1.0,
                            lastUpdate: Date.now()
                        });

                        return {
                            symbol: s.symbol,
                            score: s.score,
                            tradesPerMin: s.tradesPerMin,
                            trades5m: s.trades5m || 0,
                            acceleration: s.acceleration || 1.0,
                            natr: s.natr || 0,
                            spreadPercent: s.spreadPercent || 0,
                            largePrintCount5m: s.largePrintCount5m || 0,
                            lastLargePrintRatio: s.lastLargePrintRatio || 0,
                            lastPrice: s.lastPrice,
                            lastUpdate: s.lastUpdate
                        };
                    });

                // ANTI-FLICKER FIX: Only render page on first load
                // After that, Smart Sort will handle re-rendering if enabled
                if (window.isFirstLoad) {
                    renderPage();
                    window.isFirstLoad = false;
                    console.log('[Screener] Initial render complete. Flicker protection enabled.');
                }
                // Data is updated in allSymbols, but no re-render unless Smart Sort triggers it
            }
        } catch (error) {
            console.error('[WebSocket] Parse error:', error);
        }
    };

    window.globalWebSocket.onclose = () => {
        console.log('[WebSocket] Disconnected');

        // SPRINT-4: Exponential backoff reconnection
        const delay = Math.min(1000 * Math.pow(2, window.reconnectAttempt), MAX_RECONNECT_DELAY);
        window.reconnectAttempt++;

        console.log(`[WebSocket] Reconnecting in ${delay}ms (attempt ${window.reconnectAttempt})...`);
        if (statusText) {
            statusText.textContent = "Reconnecting...";
        }

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
    // Get trades5m from server data (same source as table)
    // This ensures charts and table show identical values
    const symbolData = window.allSymbols.find(s => s.symbol === symbol);
    const trades5m = symbolData?.trades5m || 0;
    const natr = symbolData?.natr || 0;
    const spreadPercent = symbolData?.spreadPercent || 0;

    // Update UI
    const priceEl = document.getElementById(`price-${symbol}`);
    const statsEl = document.getElementById(`stats-${symbol}`);
    const accelEl = document.getElementById(`accel-${symbol}`);

    if (priceEl) {
        priceEl.innerHTML = `<span class="price-val" style="font-family: 'JetBrains Mono';">${formatTickSize(price)}</span>`;
    }

    if (statsEl) {
        // SPRINT-11: Show NATR alongside Spread in charts view
        const natrText = natr > 0 ? `NATR: ${natr.toFixed(2)}%` : 'NATR: ---';
        statsEl.textContent = `${trades5m}/5m | ${natrText} | Spread: ${spreadPercent.toFixed(3)}%`;
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

    // NOTE: We use server-provided trades5m from all_symbols_scored
    // This is the SAME data source as the table view, ensuring consistency
    // No local counting needed - server already calculated it
}

function formatTickSize(tick) {
    if (!tick || tick <= 0) return '---';
    if (tick >= 1) return tick.toFixed(2);
    if (tick >= 0.01) return tick.toFixed(4);
    if (tick >= 0.0001) return tick.toFixed(6);
    return tick.toFixed(8).replace(/\.?0+$/, '');
}

// SMART SORTING - Export to window for onclick handlers
window.toggleSmartSort = function() {
    window.smartSortEnabled = !window.smartSortEnabled;
    const btn = document.getElementById('btnSortToggle');
    const icon = document.getElementById('sortIcon');
    btn.classList.toggle('active');

    if (window.smartSortEnabled) {
        icon.textContent = '🔥';
        btn.innerHTML = '<span id="sortIcon">🔥</span> Live Sort';
        startSmartSort();
    } else {
        icon.textContent = '❄️';
        btn.innerHTML = '<span id="sortIcon">❄️</span> Frozen';
        stopSmartSort();
    }
};

function startSmartSort() {
    if (window.smartSortInterval) clearInterval(window.smartSortInterval);
    // ANTI-FLICKER: 10 seconds instead of 2 - less aggressive re-sorting
    window.smartSortInterval = setInterval(reorderCardsWithoutDestroy, 10000);
}

function stopSmartSort() {
    if (window.smartSortInterval) {
        clearInterval(window.smartSortInterval);
        window.smartSortInterval = null;
    }
}


function reorderCardsWithoutDestroy() {
    if (!window.smartSortEnabled) return;

    // Sort by trades5m from server data
    // Descending order - most active coins first
    window.allSymbols.sort((a, b) => {
        return (b.trades5m || 0) - (a.trades5m || 0); // Descending - most active first
    });

    // Re-render with sorted data (TOP-30 displayed)
    renderPage();
}

// SPRINT-5: Health Check Functions
function startHealthCheck() {
    if (window.healthCheckInterval) clearInterval(window.healthCheckInterval);

    window.healthCheckInterval = setInterval(() => {
        const timeSinceLastTrade = Date.now() - window.lastTradeTimestamp;

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

// EXPORT FUNCTIONS for embedded mode (called by parent index.html)
window.handleAllSymbolsScored = function (msg) {
    // Update allSymbols from parent data
    window.allSymbols = msg.symbols
        .filter(s => !BLACKLIST.includes(s.symbol.toUpperCase()))
        .map(s => ({
            symbol: s.symbol,
            score: s.score,
            tradesPerMin: s.tradesPerMin,
            trades5m: s.trades5m || 0,
            acceleration: s.acceleration || 1.0,
            spreadPercent: s.spreadPercent || 0,
            lastPrice: s.lastPrice,
            lastUpdate: s.lastUpdate
        }));

    // Render page if first load
    if (window.isFirstLoad) {
        renderPage();
        window.isFirstLoad = false;
        console.log('[Screener] Initial render complete (embedded mode)');
    }
};

window.handleTradeAggregate = function (msg) {
    // Handle trade aggregates in embedded mode
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

        window.lastTradeTimestamp = Date.now();

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
};

// SPRINT-14: Handle large print events with flash animation and badge
function handleLargePrint(msg) {
    const symbol = msg.symbol.replace('MEXC_', '');

    // Find card for this symbol
    const card = document.querySelector(`[data-symbol="${symbol}"]`)?.closest('.card');
    if (!card) return; // Symbol not currently visible

    console.log(`[LargePrint] ${symbol}: ${msg.ratio.toFixed(1)}x ${msg.side} @ ${msg.price} (Vol: $${msg.volumeUSD.toFixed(2)})`);

    // Flash animation on card
    card.classList.add('large-print-flash');
    setTimeout(() => card.classList.remove('large-print-flash'), 3000);

    // Create badge with ratio and side
    const cardHeader = card.querySelector('.card-header');
    if (!cardHeader) return;

    // Remove existing badge if present
    const existingBadge = cardHeader.querySelector('.large-print-badge');
    if (existingBadge) {
        existingBadge.remove();
    }

    const badge = document.createElement('div');
    badge.className = 'large-print-badge';
    badge.innerHTML = `⚡ ${msg.ratio.toFixed(1)}x ${msg.side}`;
    cardHeader.appendChild(badge);

    // Auto-remove badge after 10 seconds
    setTimeout(() => badge.remove(), 10000);
}

// Handle price breakthrough events with flash animation and badge
function handlePriceBreakthrough(msg) {
    const symbol = msg.symbol.replace('MEXC_', '');

    // Find card for this symbol
    const card = document.querySelector(`[data-symbol="${symbol}"]`)?.closest('.card');
    if (!card) return; // Symbol not currently visible

    console.log(`[PriceBreakthrough] ${symbol}: ${msg.direction} ${msg.priceChange.toFixed(2)}% in ${msg.timeSpan}s (Start: $${msg.startPrice.toFixed(6)}, End: $${msg.endPrice.toFixed(6)}, Vol: $${msg.volumeInBreakthrough.toFixed(2)})`);

    // Flash animation on card (different color for breakthroughs)
    card.classList.add('price-breakthrough-flash');
    setTimeout(() => card.classList.remove('price-breakthrough-flash'), 3000);

    // Create badge with change and direction
    const cardHeader = card.querySelector('.card-header');
    if (!cardHeader) return;

    // Remove existing badge if present
    const existingBadge = cardHeader.querySelector('.price-breakthrough-badge');
    if (existingBadge) {
        existingBadge.remove();
    }

    const badge = document.createElement('div');
    badge.className = 'price-breakthrough-badge';
    badge.innerHTML = `🚀 ${msg.priceChange.toFixed(1)}% ${msg.direction}`;
    cardHeader.appendChild(badge);

    // Auto-remove badge after 10 seconds
    setTimeout(() => badge.remove(), 10000);
}

// Start (only in standalone mode)
if (!isEmbeddedMode) {
    init();
    startSmartSort();
    startHealthCheck();
} else {
    console.log('[Screener] Embedded mode - waiting for parent to feed data');
    // Start batching loop and health check even in embedded mode
    batchingLoop(); // Already started globally, but ensure it runs
    startSmartSort();
    startHealthCheck();
}
