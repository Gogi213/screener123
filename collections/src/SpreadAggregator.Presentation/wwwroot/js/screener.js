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
const activeCharts = new Map();     // Symbol -> Chart.js Instance

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
    // Destroy Chart.js instances properly
    activeCharts.forEach(chart => {
        chart.destroy();
    });
    activeCharts.clear();
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
        <div class="chart-container">
            <canvas id="chart-${symbol}"></canvas>
        </div>
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

    // --- CHART.JS INITIALIZATION ---
    const ctx = document.getElementById(`chart-${symbol}`).getContext('2d');

    const chart = new Chart(ctx, {
        type: 'scatter',
        data: {
            datasets: [
                {
                    label: 'Buy',
                    data: [],
                    backgroundColor: '#10b981',
                    pointRadius: 2,
                    pointHoverRadius: 3
                },
                {
                    label: 'Sell',
                    data: [],
                    backgroundColor: '#ef4444',
                    pointRadius: 2,
                    pointHoverRadius: 3
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false, // CRITICAL: Disable animation for performance
            parsing: false, // Direct data access
            normalized: true,
            plugins: {
                legend: { display: false },
                tooltip: { enabled: false } // Disable tooltips for speed
            },
            scales: {
                x: {
                    type: 'linear', // Use linear scale for timestamps
                    display: false // Hide X axis
                    // min/max will be set dynamically based on data
                },
                y: {
                    display: false, // Hide Y axis
                    beginAtZero: false
                }
            }
        }
    });

    activeCharts.set(symbol, chart);
}

// --- BATCHING LOOP (300ms) ---
setInterval(() => {
    if (pendingChartUpdates.size === 0) return;

    pendingChartUpdates.forEach((trades, symbol) => {
        const chart = activeCharts.get(symbol);
        if (chart && trades.length > 0) {
            // Add all accumulated trades
            trades.forEach(t => addTradeToChart(chart, t));

            // Update stats
            const lastTrade = trades[trades.length - 1];
            updateCardStats(symbol, lastTrade.price, chart);

            // Update Chart (Efficiently)
            // Dynamic X-axis scaling based on actual data
            const allX = [
                ...chart.data.datasets[0].data.map(p => p.x),
                ...chart.data.datasets[1].data.map(p => p.x)
            ];

            if (allX.length > 0) {
                const minX = Math.min(...allX);
                const maxX = Math.max(...allX);
                const range = maxX - minX || 1000; // Fallback if all trades at same time
                const padding = range * 0.05; // 5% padding

                chart.options.scales.x.min = minX - padding;
                chart.options.scales.x.max = maxX + padding;
            }

            chart.update('none'); // Update without animation
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

function addTradeToChart(chart, trade) {
    // Parse timestamp (ms)
    let timestamp = typeof trade.timestamp === 'string'
        ? new Date(trade.timestamp).getTime()
        : trade.timestamp; // Assume ms if number, or fix if seconds

    // Check if timestamp is seconds (small number)
    if (timestamp < 10000000000) timestamp *= 1000;

    const price = parseFloat(trade.price);
    const isBuy = (trade.side === "Buy" || trade.side === 0 || trade.side === "buy");

    if (isNaN(price)) return;

    // Add to appropriate dataset (scatter chart - independent datasets)
    // Dataset 0: Buy, Dataset 1: Sell
    const dataset = isBuy ? chart.data.datasets[0] : chart.data.datasets[1];
    dataset.data.push({ x: timestamp, y: price });

    // Cleanup old data to prevent memory bloat
    if (dataset.data.length > 2000) {
        dataset.data.shift();
    }
}

function updateCardStats(symbol, price, chart) {
    // 1. Calculate Trades/Min
    const oneMinuteAgo = Date.now() - 60000;
    let count = 0;

    // Count from dataset 0 (Buy) + dataset 1 (Sell)
    const buys = chart.data.datasets[0].data;
    const sells = chart.data.datasets[1].data;

    // Iterate backwards (all values are valid in scatter chart)
    for (let i = buys.length - 1; i >= 0; i--) {
        if (buys[i].x < oneMinuteAgo) break;
        count++;
    }
    for (let i = sells.length - 1; i >= 0; i--) {
        if (sells[i].x < oneMinuteAgo) break;
        count++;
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
