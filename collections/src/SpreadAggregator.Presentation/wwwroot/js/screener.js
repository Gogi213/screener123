// CONFIG
const ITEMS_PER_PAGE = 100;
const HISTORY_MINUTES = 30;

// BLACKLIST (Pairs to hide)
const BLACKLIST = [
    'BTCUSDT', 'ETHUSDT', 'BTCUSDE', 'USDCUSDT', 'ETHUSDC',
    'BNBUSDT', 'DOGEUSDT', 'SOLUSDT', 'XRPUSDC', 'SUIUSDT', 'DOGEUSDE',
    'FDUSDUSDT', 'CRVUSDC' // Added per user request
].map(s => s.toUpperCase());

// STATE
let allSymbols = [];
let currentPage = 1;
const activeCharts = new Map();     // Symbol -> ApexCharts Instance

// SMART SORTING STATE
let smartSortEnabled = true; // Smart sorting is ON by default
const symbolActivity = new Map(); // Symbol -> { trades1m: number, lastUpdate: timestamp }
let smartSortInterval = null;

// GLOBAL WebSocket connection (FleckWebSocketServer broadcasts ALL trades)
let globalWebSocket = null;

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
    activeCharts.forEach(chart => chart.destroy());
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

    // Enable/disable First/Last buttons
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
        <!-- MEANINGFUL INFO: Tick Size -->
        <div class="price-info" id="price-${symbol}">
            <span class="price-val">---</span>
        </div>
        <div class="chart-container">
            <div id="chart-${symbol}" style="width: 100%; height: 100%;"></div>
        </div>
    `;
    grid.appendChild(card);

    // Add click-to-copy functionality
    const symbolEl = card.querySelector('.symbol-name');
    symbolEl.addEventListener('click', async () => {
        try {
            await navigator.clipboard.writeText(symbol);
            // Visual feedback
            const originalColor = symbolEl.style.color;
            symbolEl.style.color = '#10b981';
            setTimeout(() => {
                symbolEl.style.color = originalColor;
            }, 200);
        } catch (err) {
            console.error('Failed to copy:', err);
        }
    });

    // SPRINT 0: ApexCharts initialization
    const options = {
        series: [
            {
                name: 'Buy',
                data: []
            },
            {
                name: 'Sell',
                data: []
            }
        ],
        chart: {
            type: 'scatter',
            height: '100%',
            animations: {
                enabled: false
            },
            toolbar: {
                show: false
            },
            zoom: {
                enabled: true,
                type: 'x'
            }
        },
        colors: ['#10b981', '#ef4444'],
        markers: {
            size: 2,
            hover: {
                size: 4
            }
        },
        xaxis: {
            type: 'datetime',
            labels: {
                show: false
            },
            axisBorder: {
                show: false
            },
            axisTicks: {
                show: false
            }
        },
        yaxis: {
            opposite: true,
            labels: {
                style: {
                    colors: '#666',
                    fontSize: '9px'
                }
            }
        },
        grid: {
            show: false
        },
        legend: {
            show: false
        },
        tooltip: {
            enabled: true,
            shared: false,
            x: {
                format: 'HH:mm:ss'
            }
        }
    };

    const chartElement = document.getElementById(`chart-${symbol}`);
    const chart = new ApexCharts(chartElement, options);
    chart.render();

    activeCharts.set(symbol, chart);
}

// GLOBAL WebSocket connection (FleckWebSocketServer broadcasts to all clients)
function initGlobalWebSocket() {
    if (globalWebSocket && globalWebSocket.readyState === WebSocket.OPEN) {
        return; // Already connected
    }

    globalWebSocket = new WebSocket(`ws://${window.location.hostname}:8181`);

    globalWebSocket.onopen = () => {
        console.log('[WebSocket] Connected to broadcast server on port 8181');
        statusText.textContent = `Live: ${allSymbols.length} Pairs`;
    };

    globalWebSocket.onmessage = (event) => {
        const msg = JSON.parse(event.data);

        // Route trade_update messages to appropriate chart
        if (msg.type === 'trade_update' && msg.symbol) {
            const symbol = msg.symbol.replace('MEXC_', ''); // Remove exchange prefix
            const chart = activeCharts.get(symbol);

            if (chart && msg.trades) {
                msg.trades.forEach(t => addTradeToChart(chart, t));
                if (msg.trades.length > 0) {
                    const lastTrade = msg.trades[msg.trades.length - 1];
                    updateCardStats(symbol, lastTrade.price, chart);
                }
            }
        }
    };

    globalWebSocket.onclose = () => {
        console.log('[WebSocket] Disconnected, reconnecting in 3s...');
        statusText.textContent = "Reconnecting...";
        setTimeout(initGlobalWebSocket, 3000);
    };

    globalWebSocket.onerror = (error) => {
        console.error('[WebSocket] Error:', error);
    };
}

function addTradeToChart(chart, trade) {
    // Parse timestamp if it's a string
    const timestamp = typeof trade.timestamp === 'string'
        ? new Date(trade.timestamp).getTime()
        : trade.timestamp;

    const point = { x: timestamp, y: trade.price };
    const seriesIndex = (trade.side === "Buy" || trade.side === 0) ? 0 : 1;

    // Get current series data
    const currentSeries = chart.w.config.series.map((s, idx) => {
        const data = [...s.data];
        if (idx === seriesIndex) {
            data.push(point);
        }
        return { name: s.name, data: data };
    });

    // Remove old data (30 min window)
    const threshold = Date.now() - (HISTORY_MINUTES * 60 * 1000);
    currentSeries.forEach(s => {
        while (s.data.length > 0 && s.data[0].x < threshold) {
            s.data.shift();
        }
    });

    chart.updateSeries(currentSeries, false);
}

function updateCardStats(symbol, price, chart) {
    const priceEl = document.getElementById(`price-${symbol}`);

    // 1. Calculate Buy/Sell Pressure (for sorting only, not displayed)
    const oneMinuteAgo = Date.now() - 60000;
    let buyCount = 0;
    let sellCount = 0;

    const series = chart.w.config.series;
    for (const p of series[0].data) { if (p.x >= oneMinuteAgo) buyCount++; }
    for (const p of series[1].data) { if (p.x >= oneMinuteAgo) sellCount++; }

    const total = buyCount + sellCount;

    // 2. Calculate Tick Size (minimum price step)
    const tickSize = calculateTickSize(chart);

    // 3. Update UI - ONLY Tick Size (pressure removed per user request)
    if (priceEl) {
        priceEl.innerHTML = `<span class="price-val" style="font-family: 'JetBrains Mono';">${tickSize}</span>`;
    }

    const statsEl = document.getElementById(`stats-${symbol}`);
    if (statsEl) {
        statsEl.textContent = `${total}/1m`;
    }

    // SMART SORTING: Update activity tracker
    updateSymbolActivity(symbol, total);
}

// Calculate Tick Size from chart data (minimum price difference)
function calculateTickSize(chart) {
    const allPrices = [];

    // Collect all unique prices from last 100 points
    const series = chart.w.config.series;
    const buyData = series[0].data.slice(-100);
    const sellData = series[1].data.slice(-100);

    buyData.forEach(p => allPrices.push(p.y));
    sellData.forEach(p => allPrices.push(p.y));

    if (allPrices.length < 2) return '---';

    // Sort and find unique prices
    const uniquePrices = [...new Set(allPrices)].sort((a, b) => a - b);

    if (uniquePrices.length < 2) return formatTickSize(uniquePrices[0]);

    // Find minimum difference between consecutive prices
    let minDiff = Infinity;
    for (let i = 1; i < uniquePrices.length; i++) {
        const diff = uniquePrices[i] - uniquePrices[i - 1];
        if (diff > 0 && diff < minDiff) {
            minDiff = diff;
        }
    }

    return minDiff === Infinity ? '---' : formatTickSize(minDiff);
}

// Format tick size with appropriate precision
function formatTickSize(tick) {
    if (!tick || tick <= 0) return '---';

    // Determine precision based on tick size magnitude
    if (tick >= 1) return tick.toFixed(2);
    if (tick >= 0.01) return tick.toFixed(4);
    if (tick >= 0.0001) return tick.toFixed(6);

    // For very small ticks
    return tick.toFixed(8).replace(/\.?0+$/, '');
}

// SMART SORTING FUNCTIONS
function toggleSmartSort() {
    smartSortEnabled = !smartSortEnabled;
    const btn = document.getElementById('btnSortToggle');
    const icon = document.getElementById('sortIcon');

    if (smartSortEnabled) {
        btn.classList.add('active');
        icon.textContent = '🔥';
        startSmartSort();
    } else {
        btn.classList.remove('active');
        icon.textContent = '❄️';
        stopSmartSort();
    }
}

function startSmartSort() {
    if (smartSortInterval) return; // Already running

    smartSortInterval = setInterval(() => {
        if (!smartSortEnabled) return;

        // Sort allSymbols by trades per minute (descending)
        allSymbols.sort((a, b) => {
            const aActivity = symbolActivity.get(a.symbol) || { trades1m: 0 };
            const bActivity = symbolActivity.get(b.symbol) || { trades1m: 0 };
            return bActivity.trades1m - aActivity.trades1m;
        });

        // Reorder DOM without destroying charts/WebSockets
        reorderCardsWithoutDestroy();

    }, 2000); // Smart sort every 2 seconds
}

// PERFORMANCE FIX: Reorder cards without destroying charts/WebSockets
function reorderCardsWithoutDestroy() {
    const start = (currentPage - 1) * ITEMS_PER_PAGE;
    const end = start + ITEMS_PER_PAGE;
    const pageSymbols = allSymbols.slice(start, end);

    const cards = Array.from(grid.children);

    // Create a map of symbol -> card element
    const cardMap = new Map();
    cards.forEach(card => {
        const symbolEl = card.querySelector('.symbol-name');
        if (symbolEl) {
            cardMap.set(symbolEl.textContent, card);
        }
    });

    // Detach all cards from DOM (but don't destroy!)
    cards.forEach(card => card.remove());

    // Reattach cards in new sorted order
    pageSymbols.forEach(s => {
        const card = cardMap.get(s.symbol);
        if (card) {
            grid.appendChild(card);
        }
    });
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

// Start
init();

// Start smart sorting on page load
if (smartSortEnabled) {
    startSmartSort();
}
