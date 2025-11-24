// CONFIG
const ITEMS_PER_PAGE = 100;
const HISTORY_MINUTES = 30;

// STATE
let allSymbols = [];
let currentPage = 1;
const activeWebSockets = new Map(); // Symbol -> WebSocket
const activeCharts = new Map();     // Symbol -> Chart Instance

// DOM ELEMENTS
const grid = document.getElementById('grid');
const statusText = document.getElementById('status-text');
const btnPrev = document.getElementById('btnPrev');
const btnNext = document.getElementById('btnNext');
const pageIndicator = document.getElementById('pageIndicator');

// UTILS
function formatPrice(price) {
    if (!price) return '0.00';
    if (price < 0.0001) return price.toFixed(8); // Small caps
    if (price < 1) return price.toFixed(6);
    if (price < 10) return price.toFixed(4);
    return price.toFixed(2);
}

function formatVolume(val) {
    if (!val) return '0';
    if (val >= 1000000) return (val / 1000000).toFixed(1) + 'M';
    if (val >= 1000) return (val / 1000).toFixed(1) + 'K';
    return val.toString();
}

// CORE LOGIC
async function init() {
    try {
        statusText.textContent = "Fetching symbols...";
        const response = await fetch('/api/trades/symbols');
        allSymbols = await response.json();

        statusText.textContent = `Live: ${allSymbols.length} Pairs`;

        // Initial Render
        renderPage();

    } catch (e) {
        console.error("Init error:", e);
        statusText.textContent = "Connection Error";
        setTimeout(init, 5000);
    }
}

function cleanupPage() {
    // 1. Close all WebSockets
    activeWebSockets.forEach(ws => {
        if (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING) {
            ws.close();
        }
    });
    activeWebSockets.clear();

    // 2. Destroy all Charts (CRITICAL for WebGL memory)
    activeCharts.forEach(chart => chart.destroy());
    activeCharts.clear();

    // 3. Clear DOM
    grid.innerHTML = '';
}

function renderPage() {
    cleanupPage();

    const totalPages = Math.ceil(allSymbols.length / ITEMS_PER_PAGE);

    // Validate page bounds
    if (currentPage < 1) currentPage = 1;
    if (currentPage > totalPages) currentPage = totalPages;

    // Update Controls
    pageIndicator.textContent = `Page ${currentPage} of ${totalPages}`;
    btnPrev.disabled = currentPage === 1;
    btnNext.disabled = currentPage === totalPages;
    statusText.textContent = `Live: ${allSymbols.length} Pairs (showing ${ITEMS_PER_PAGE})`;

    // Slice Data
    const start = (currentPage - 1) * ITEMS_PER_PAGE;
    const end = start + ITEMS_PER_PAGE;
    const pageSymbols = allSymbols.slice(start, end);

    // Render Cards
    pageSymbols.forEach(s => createCard(s.symbol, s.tradeCount));
}

function changePage(delta) {
    currentPage += delta;
    renderPage();
    // Scroll to top
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function createCard(symbol, initialTradeCount) {
    const card = document.createElement('div');
    card.className = 'card';
    card.innerHTML = `
        <div class="card-header">
            <div class="symbol-name">${symbol}</div>
            <div class="trade-stats" id="stats-${symbol}">0/1m</div>
        </div>
        <div class="price-info" id="price-${symbol}">---</div>
        <div class="chart-container">
            <canvas id="chart-${symbol}"></canvas>
        </div>
    `;
    grid.appendChild(card);

    const ctx = document.getElementById(`chart-${symbol}`).getContext('2d');

    const chart = new Chart(ctx, {
        type: 'scatter',
        data: {
            datasets: [
                {
                    label: 'Buy',
                    data: [],
                    backgroundColor: '#10b981', // Green
                    pointRadius: 2,
                    pointHoverRadius: 4
                },
                {
                    label: 'Sell',
                    data: [],
                    backgroundColor: '#ef4444', // Red
                    pointRadius: 2,
                    pointHoverRadius: 4
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false, // Performance
            plugins: {
                legend: { display: false },
                tooltip: { enabled: false } // Performance
            },
            scales: {
                x: {
                    type: 'time',
                    time: { unit: 'minute' },
                    display: false, // Clean look
                    grid: { display: false }
                },
                y: {
                    display: true, // Show Price Axis
                    position: 'right',
                    grid: { display: false, color: '#333' },
                    ticks: { color: '#888', font: { size: 10 } }
                }
            }
        }
    });

    // Save chart instance for cleanup
    activeCharts.set(symbol, chart);

    connectWebSocket(symbol, chart);
}

function connectWebSocket(symbol, chart) {
    // Double check cleanup (just in case)
    const oldWs = activeWebSockets.get(symbol);
    if (oldWs) oldWs.close();

    // FIX: Use Path Parameter (/ws/trades/SYMBOL) instead of Query Parameter
    const ws = new WebSocket(`ws://${window.location.hostname}:5000/ws/trades/${symbol}`);
    activeWebSockets.set(symbol, ws);

    ws.onmessage = (event) => {
        const msg = JSON.parse(event.data);

        // FIX: Handle 'snapshot' and 'update' message types
        if (msg.type === 'snapshot') {
            msg.trades.forEach(t => addTradeToChart(chart, t));
        } else if (msg.type === 'update') {
            addTradeToChart(chart, msg.trade);
            updateCardStats(symbol, msg.trade.price, chart);
        }

        chart.update('none');
    };

    ws.onclose = () => {
        // Auto-reconnect only if still on same page
        if (activeWebSockets.get(symbol) === ws) {
            setTimeout(() => connectWebSocket(symbol, chart), 3000);
        }
    };
}

function addTradeToChart(chart, trade) {
    const point = { x: trade.timestamp, y: trade.price };

    // Add point
    // FIX: Check for both string "Buy" (from API) and integer 0 (legacy)
    if (trade.side === "Buy" || trade.side === 0) {
        chart.data.datasets[0].data.push(point);
    } else { // Sell
        chart.data.datasets[1].data.push(point);
    }

    // Efficient Cleanup (Shift instead of Filter)
    const threshold = Date.now() - (HISTORY_MINUTES * 60 * 1000);

    // Cleanup Buys
    while (chart.data.datasets[0].data.length > 0 && chart.data.datasets[0].data[0].x < threshold) {
        chart.data.datasets[0].data.shift();
    }
    // Cleanup Sells
    while (chart.data.datasets[1].data.length > 0 && chart.data.datasets[1].data[0].x < threshold) {
        chart.data.datasets[1].data.shift();
    }
}

function updateCardStats(symbol, price, chart) {
    const priceEl = document.getElementById(`price-${symbol}`);
    if (priceEl) priceEl.textContent = formatPrice(price);

    const statsEl = document.getElementById(`stats-${symbol}`);
    if (statsEl) {
        const oneMinuteAgo = Date.now() - 60000;
        let count = 0;

        // Manual count (faster than filter)
        for (const p of chart.data.datasets[0].data) {
            if (p.x >= oneMinuteAgo) count++;
        }
        for (const p of chart.data.datasets[1].data) {
            if (p.x >= oneMinuteAgo) count++;
        }

        statsEl.textContent = `${count}/1m`;
    }
}

// Start
init();
