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

// uPlot DATA STRUCTURE - Map<symbol, {times: Float64Array, buys: Float32Array, sells: Float32Array, index: number}>
const chartData = new Map();

// SMART SORTING STATE
let smartSortEnabled = true;
const symbolActivity = new Map(); // Symbol -> { trades1m: number, lastUpdate: timestamp }
let smartSortInterval = null;

// GLOBAL WebSocket connection
let globalWebSocket = null;

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

        // Connect global WebSocket AFTER rendering
        initGlobalWebSocket();

    } catch (e) {
        console.error("Init error:", e);
        statusText.textContent = "Connection Error";
        setTimeout(init, 5000);
    }
}

function cleanupPage() {
    // Destroy uPlot instances properly
    activeCharts.forEach(uplot => {
        uplot.destroy();
    });
    activeCharts.clear();
    chartData.clear();
    grid.innerHTML = '';
}

function renderPage() {
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
}

function changePage(delta) {
    currentPage += delta;
    renderPage();
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function goToFirstPage() {
    currentPage = 1;
    renderPage();
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function goToLastPage() {
    const totalPages = Math.ceil(allSymbols.length / ITEMS_PER_PAGE);
    currentPage = totalPages;
    renderPage();
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function createCard(symbol, initialTradeCount) {
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = `
        <div class="card-header">
            <div class="symbol-name" data-symbol="${symbol}">${symbol}</div>
            <div class="trade-stats" id="stats-${symbol}">0/1m</div>
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
        height: 150,
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
            { show: false },
            { show: false }
        ]
    };

    // Initial empty data: [timestamps[], buys[], sells[]]
    const data = [[], [], []];
    const uplot = new uPlot(opts, data, container);

    activeCharts.set(symbol, uplot);

    // Initialize data storage for this symbol
    chartData.set(symbol, {
        times: [],
        buys: [],
        sells: []
    });
}

// --- BATCHING LOOP (300ms) ---
setInterval(() => {
    if (pendingChartUpdates.size === 0) return;

    pendingChartUpdates.forEach((trades, symbol) => {
        const uplot = activeCharts.get(symbol);
        const data = chartData.get(symbol);

        if (uplot && data && trades.length > 0) {
            // Add all accumulated trades
            trades.forEach(t => addTradeToChart(symbol, t));

            // Update stats
            const lastTrade = trades[trades.length - 1];
            updateCardStats(symbol, lastTrade.price);

            // Update uPlot with new data
            uplot.setData([data.times, data.buys, data.sells]);
        }
    });

    pendingChartUpdates.clear();
}, 300);

function initGlobalWebSocket() {
    if (globalWebSocket && globalWebSocket.readyState === WebSocket.OPEN) return;

    globalWebSocket = new WebSocket(`ws://${window.location.hostname}:8181`);

    globalWebSocket.onopen = () => {
        console.log('[WebSocket] Connected to broadcast server');
        statusText.textContent = `Live: ${allSymbols.length} Pairs`;
    };

    globalWebSocket.onmessage = (event) => {
        const msg = JSON.parse(event.data);
        if (msg.type === 'trade_update' && msg.symbol) {
            const symbol = msg.symbol.replace('MEXC_', '');
            if (activeCharts.has(symbol) && msg.trades) {
                const pending = pendingChartUpdates.get(symbol) || [];
                pending.push(...msg.trades);
                pendingChartUpdates.set(symbol, pending);
            }
        }
    };

    globalWebSocket.onclose = () => {
        console.log('[WebSocket] Disconnected');
        statusText.textContent = "Reconnecting...";
        setTimeout(initGlobalWebSocket, 3000);
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

    // Cleanup old data to prevent memory bloat
    if (data.times.length > 2000) {
        data.times.shift();
        data.buys.shift();
        data.sells.shift();
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
    const priceEl = document.getElementById(`price-${symbol}`);
    const statsEl = document.getElementById(`stats-${symbol}`);

    if (priceEl) {
        priceEl.innerHTML = `<span class="price-val" style="font-family: 'JetBrains Mono';">${formatTickSize(price)}</span>`;
    }

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

    const cards = Array.from(grid.children);
    const cardsMap = new Map();
    cards.forEach(c => {
        const sym = c.querySelector('.symbol-name').dataset.symbol;
        cardsMap.set(sym, c);
    });

    const currentSymbols = Array.from(cardsMap.keys());

    // Sort by activity
    currentSymbols.sort((a, b) => {
        const actA = symbolActivity.get(a)?.trades1m || 0;
        const actB = symbolActivity.get(b)?.trades1m || 0;
        return actB - actA; // Descending
    });

    // Reorder DOM
    currentSymbols.forEach(sym => {
        const card = cardsMap.get(sym);
        grid.appendChild(card); // Moves it to the end (reordering)
    });
}

// Start
init();
startSmartSort();
