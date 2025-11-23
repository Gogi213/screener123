// CONFIG
const MAX_CHARTS = 100;
const HISTORY_MINUTES = 30;

const grid = document.getElementById('grid');
const statusText = document.getElementById('status-text');

// FORMATTER: 0.00000123 -> 0.(5)123
function formatPrice(val) {
    if (!val) return '0';
    if (val >= 1) return val.toFixed(2);
    if (val >= 0.001) return val.toFixed(5);

    const str = val.toFixed(10); // High precision
    // Match 0.000... (zeros) (rest)
    const match = str.match(/^0\.(0+)([^0].*)$/);
    if (match) {
        const zeros = match[1].length;
        const rest = match[2].substring(0, 4); // Keep 4 sig figs
        return `0.(${zeros})${rest}`;
    }
    return val.toString();
}

async function init() {
    try {
        statusText.textContent = "Fetching symbols...";
        const response = await fetch('/api/trades/symbols');
        const symbols = await response.json();

        const topSymbols = symbols.slice(0, MAX_CHARTS);

        statusText.textContent = `Live: ${topSymbols.length} Pairs`;
        grid.innerHTML = '';

        topSymbols.forEach(s => createCard(s.symbol, s.tradeCount));

    } catch (e) {
        console.error("Init error:", e);
        statusText.textContent = "Connection Error";
        setTimeout(init, 5000);
    }
}

function createCard(symbol, initialCount) {
    const div = document.createElement('div');
    div.className = 'chart-card';
    div.innerHTML = `
        <div class="card-header">
            <span class="symbol-name">${symbol}</span>
            <span class="trade-stats" id="stats-${symbol}" title="Trades per minute">0/1m</span>
        </div>
        <div class="chart-wrapper">
            <canvas id="canvas-${symbol}"></canvas>
        </div>
    `;
    grid.appendChild(div);

    const canvas = document.getElementById(`canvas-${symbol}`);
    const ctx = canvas.getContext('2d');

    const chart = new Chart(ctx, {
        type: 'scatter',
        data: {
            datasets: [
                {
                    label: 'Buy',
                    data: [],
                    backgroundColor: '#22c55e',
                    pointRadius: 1.5,
                    pointHoverRadius: 4,
                    borderWidth: 0
                },
                {
                    label: 'Sell',
                    data: [],
                    backgroundColor: '#ef4444',
                    pointRadius: 1.5,
                    pointHoverRadius: 4,
                    borderWidth: 0
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            interaction: {
                mode: 'nearest',
                intersect: false,
                axis: 'x'
            },
            scales: {
                x: {
                    type: 'time',
                    time: {
                        unit: 'minute',
                        displayFormats: { minute: 'HH:mm' }
                    },
                    grid: {
                        color: '#27272a',
                        drawBorder: false,
                        tickLength: 4
                    },
                    ticks: {
                        color: '#71717a',
                        font: { size: 9, family: "'JetBrains Mono', monospace" },
                        maxTicksLimit: 5,
                        maxRotation: 0
                    }
                },
                y: {
                    position: 'right',
                    grid: {
                        color: '#27272a',
                        drawBorder: false,
                        tickLength: 0
                    },
                    ticks: {
                        color: '#71717a',
                        font: { size: 9, family: "'JetBrains Mono', monospace" },
                        callback: (val) => formatPrice(val),
                        padding: 4
                    },
                    beginAtZero: false
                }
            },
            plugins: {
                legend: { display: false },
                tooltip: {
                    enabled: true,
                    backgroundColor: 'rgba(24, 24, 27, 0.9)',
                    titleColor: '#e4e4e7',
                    bodyColor: '#a1a1aa',
                    borderColor: '#27272a',
                    borderWidth: 1,
                    padding: 8,
                    titleFont: { size: 11, family: "'Inter', sans-serif" },
                    bodyFont: { size: 10, family: "'JetBrains Mono', monospace" },
                    callbacks: {
                        label: function (context) {
                            const p = formatPrice(context.parsed.y);
                            const t = new Date(context.parsed.x).toLocaleTimeString([], { hour12: false });
                            return `${context.dataset.label.toUpperCase()}  ${p}  ${t}`;
                        }
                    }
                },
                zoom: {
                    zoom: {
                        wheel: { enabled: true },
                        pinch: { enabled: true },
                        mode: 'x',
                    },
                    pan: {
                        enabled: true,
                        mode: 'x',
                        modifierKey: null, // No key needed
                        threshold: 10
                    }
                }
            }
        }
    });

    // Double click to reset zoom
    canvas.ondblclick = () => {
        chart.resetZoom();
    };

    connectWebSocket(symbol, chart);
}

function connectWebSocket(symbol, chart) {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${window.location.host}/ws/trades/${symbol}`);

    ws.onmessage = (event) => {
        const msg = JSON.parse(event.data);

        if (msg.type === 'snapshot') {
            const buys = [];
            const sells = [];

            msg.trades.forEach(t => {
                const point = { x: t.timestamp, y: parseFloat(t.price) };
                if (t.side === 'Buy') buys.push(point);
                else sells.push(point);
            });

            chart.data.datasets[0].data = buys;
            chart.data.datasets[1].data = sells;
            chart.update();

            // Update stats immediately
            updateCardStats(symbol, chart);
        }
        else if (msg.type === 'update') {
            const t = msg.trade;
            const point = { x: t.timestamp, y: parseFloat(t.price) };

            if (t.side === 'Buy') {
                chart.data.datasets[0].data.push(point);
            } else {
                chart.data.datasets[1].data.push(point);
            }

            // Time-based cleanup (30 mins)
            const threshold = Date.now() - HISTORY_MINUTES * 60 * 1000;

            if (chart.data.datasets[0].data.length > 0 && chart.data.datasets[0].data[0].x < threshold) {
                chart.data.datasets[0].data = chart.data.datasets[0].data.filter(p => p.x >= threshold);
            }
            if (chart.data.datasets[1].data.length > 0 && chart.data.datasets[1].data[0].x < threshold) {
                chart.data.datasets[1].data = chart.data.datasets[1].data.filter(p => p.x >= threshold);
            }

            chart.update('none');

            // Update stats dynamic (last 1 minute)
            updateCardStats(symbol, chart);
        }
    };

    ws.onclose = () => {
        setTimeout(() => connectWebSocket(symbol, chart), 5000);
    };
}

function updateCardStats(symbol, chart) {
    const el = document.getElementById(`stats-${symbol}`);
    if (!el) return;

    // Count trades in last 1 minute (sliding window)
    const oneMinuteAgo = Date.now() - 60 * 1000;
    const buyCount = chart.data.datasets[0].data.filter(p => p.x >= oneMinuteAgo).length;
    const sellCount = chart.data.datasets[1].data.filter(p => p.x >= oneMinuteAgo).length;
    const totalCount = buyCount + sellCount;

    el.textContent = `${totalCount}/1m`;
}

// Start
init();
