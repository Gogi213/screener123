// CONFIG
const ITEMS_PER_PAGE = 100;
const HISTORY_MINUTES = 30;

// BLACKLIST (Pairs to hide)
const BLACKLIST = [
    'BTCUSDT', 'ETHUSDT', 'BTCUSDE', 'USDCUSDT', 'ETHUSDC',
    'BNBUSDT', 'DOGEUSDT', 'SOLUSDT', 'XRPUSDC', 'SUIUSDT', 'DOGEUSDE',
    'FDUSDUSDT', 'CRVUSDC'
].map(s => s.toUpperCase());

// STATE
let allSymbols = [];
let currentPage = 1;
const activeCharts = new Map();     // Symbol -> uPlot Instance

// uPlot DATA STRUCTURE - Map<symbol, {times: [], buys: [], sells: [], startIndex: number}>
const chartData = new Map();

// SMART SORTING STATE
let smartSortEnabled = true;
const symbolActivity = new Map(); // Symbol -> { trades1m: number, lastUpdate: timestamp }
let smartSortInterval = null;

// BATCHING STATE
const pendingChartUpdates = new Map(); // symbol -> [trades]

// DOM ELEMENTS
const grid = document.getElementById('grid');
const statusText = document.getElementById('status-text');
const btnPrev = document.getElementById('btnPrev');
const btnNext = document.getElementById('btnNext');
const pageIndicator = document.getElementById('pageIndicator');

// CORE LOGIC
async function init() {
    try {
        statusText.textContent = "Fetching symbols...";
        const response = await fetch('/api/trades/symbols');
        const data = await response.json();

        // FILTER: Remove Blacklisted pairs
        allSymbols = data.filter(s => !BLACKLIST.includes(s.symbol.toUpperCase()));

        statusText.textContent = `Live: ${allSymbols.length} Pairs`;

        // Initial Render
        renderPage();

        // Connect gRPC stream AFTER rendering
        initGrpcStream();

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
    // NOTE: chartData is NOT cleared - data persists across pages for all symbols
    grid.innerHTML = '';
}

function renderPage(autoScroll = false) {
    // Save current scroll position before cleanup
    const savedScrollY = window.scrollY;

    cleanupPage();

    const totalPages = Math.ceil(allSymbols.length / ITEMS_PER_PAGE);

    if (currentPage < 1) currentPage = 1;
    if (currentPage > totalPages) currentPage = totalPages;

    pageIndicator.textContent = `Page ${currentPage} of ${totalPages}`;
    btnPrev.disabled = currentPage === 1;
    btnNext.disabled = currentPage === totalPages;

    const btnFirst = document.getElementById('btnFirst');
    const btnLast = document.getElementById('btnLast');
    if (btnFirst) btnFirst.disabled = currentPage === 1;
    if (btnLast) btnLast.disabled = currentPage === totalPages;

    statusText.textContent = `Live: ${allSymbols.length} Pairs (showing ${ITEMS_PER_PAGE})`;

    const start = (currentPage - 1) * ITEMS_PER_PAGE;
    const end = start + ITEMS_PER_PAGE;
    const pageSymbols = allSymbols.slice(start, end);

    pageSymbols.forEach(s => createCard(s.symbol, s.tradeCount));

    // Handle scroll: either restore saved position or scroll to top
    if (autoScroll) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    } else {
        // Restore scroll position (for smart sort - don't jump)
        window.scrollTo({ top: savedScrollY, behavior: 'instant' });
    }
}

function changePage(delta) {
    currentPage += delta;
    renderPage(true); // Auto-scroll on manual page change

    // Restart gRPC stream for new page (server-side filtering!)
    initGrpcStream();
}

function goToFirstPage() {
    currentPage = 1;
    renderPage(true); // Auto-scroll on manual page change

    // Restart gRPC stream for new page
    initGrpcStream();
}

function goToLastPage() {
    const totalPages = Math.ceil(allSymbols.length / ITEMS_PER_PAGE);
    currentPage = totalPages;
    renderPage(true); // Auto-scroll on manual page change

    // Restart gRPC stream for new page
    initGrpcStream();
}

function createCard(symbol, initialTradeCount) {
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = `
        <div class="card-header">
            <div class="symbol-name" data-symbol="${symbol}">${symbol}</div>
            <div class="trade-stats" id="stats-${symbol}">0/1m</div>
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
                stroke: '#10b981',
                fill: '#10b98120',
                points: { show: true, size: 3, width: 1 }
            },
            {
                label: 'Sell',
                stroke: '#ef4444',
                fill: '#ef444420',
                points: { show: true, size: 3, width: 1 }
            }
        ],
        axes: [
            { show: false }, // X axis (time) - hidden
            {
                show: true,
                side: 1,
                stroke: '#6b7280', // Светло-серый цвет для линий оси
                grid: {
                    show: true,
                    stroke: '#374151', // Темно-серый для сетки
                    width: 1
                },
                ticks: {
                    show: true,
                    stroke: '#6b7280', // Светло-серый для делений
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

    // Initialize stats from existing data (if any) to prevent "0/1m" and "---"
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

// --- BATCHING LOOP (requestAnimationFrame with 300ms throttle) ---
let lastBatchUpdate = 0;

function batchingLoop() {
    const now = performance.now();

    // Throttle to ~300ms (don't update more often than needed)
    if (now - lastBatchUpdate > 300 && pendingChartUpdates.size > 0) {
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
// gRPC-WEB STREAMING (replaces WebSocket)
// ===================================================================
let grpcClient = null;
let currentGrpcStream = null;

function initGrpcStream() {
    // Initialize gRPC client (once)
    if (!grpcClient) {
        const grpcHost = `http://${window.location.hostname}:${window.location.port || 5000}`;
        grpcClient = new GrpcTradeClient(grpcHost);
        console.log(`[gRPC] Client initialized: ${grpcHost}`);
    }

    // Stop previous stream if exists
    if (currentGrpcStream) {
        currentGrpcStream.abort();
    }

    console.log(`[gRPC] Starting stream for page ${currentPage}, pageSize ${ITEMS_PER_PAGE}`);
    statusText.textContent = `Connecting (gRPC)...`;

    // Start streaming trades for current page
    grpcClient.streamTrades(
        currentPage,
        ITEMS_PER_PAGE,
        (tradeUpdate) => {
            // SUCCESS: Received TradeUpdate from server
            // tradeUpdate = { symbol: "MEXC_BTCUSDT", trades: [{price, quantity, side, timestamp}] }
            const symbol = tradeUpdate.symbol.replace('MEXC_', '');

            // Accumulate trades for batching
            const pending = pendingChartUpdates.get(symbol) || [];
            pending.push(...tradeUpdate.trades);
            pendingChartUpdates.set(symbol, pending);

            // Update status on first message
            if (statusText.textContent.includes('Connecting')) {
                statusText.textContent = `Live: ${allSymbols.length} Pairs (gRPC)`;
            }
        },
        (error) => {
            // ERROR: Stream failed
            console.error('[gRPC] Stream error:', error);
            statusText.textContent = `⚠️ Connection lost (gRPC)`;

            // Retry after 3 seconds
            setTimeout(initGrpcStream, 3000);
        }
    );
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

    // 1. Calculate Trades/Min
    const oneMinuteAgo = Date.now() - 60000;
    let count = 0;

    // Count from times array (iterate backwards)
    for (let i = data.times.length - 1; i >= 0; i--) {
        if (data.times[i] < oneMinuteAgo) break;
        // Count non-null values in buys or sells
        if (data.buys[i] !== null || data.sells[i] !== null) {
            count++;
        }
    }

    // 2. Update UI
    const statsEl = document.getElementById(`stats-${symbol}`);

    if (statsEl) {
        statsEl.textContent = `${count}/1m`;
    }

    // Smart Sort Update
    updateSymbolActivity(symbol, count);
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
    btn.classList.toggle('active');

    if (smartSortEnabled) {
        startSmartSort();
    } else {
        stopSmartSort();
    }
}

function startSmartSort() {
    if (smartSortInterval) clearInterval(smartSortInterval);
    smartSortInterval = setInterval(reorderCardsWithoutDestroy, 2000);
}

function stopSmartSort() {
    if (smartSortInterval) {
        clearInterval(smartSortInterval);
        smartSortInterval = null;
    }
}

function updateSymbolActivity(symbol, trades1m) {
    symbolActivity.set(symbol, {
        trades1m: trades1m,
        lastUpdate: Date.now()
    });
}

function reorderCardsWithoutDestroy() {
    if (!smartSortEnabled) return;

    // GLOBAL SORT: Sort ALL 2000+ symbols by activity, not just current page!
    allSymbols.sort((a, b) => {
        const actA = symbolActivity.get(a.symbol)?.trades1m || 0;
        const actB = symbolActivity.get(b.symbol)?.trades1m || 0;
        return actB - actA; // Descending - most active first
    });

    // Re-render current page with globally sorted data
    // Page 1 = top 1-100, Page 2 = top 101-200, etc.
    renderPage();
}

// Start
init();
startSmartSort();
