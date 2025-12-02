// Bid/Bid Deviation Charts - All 9 Symbols
// With localStorage persistence across page reloads

console.log('[Charts] Initializing all deviation charts...');

const SYMBOLS = ['BTC_USDT', 'ETH_USDT', 'SOL_USDT', 'ZEC_USDT', 'SUI_USDT', 'ASTER_USDT', 'DOGE_USDT', 'HYPE_USDT', 'LINK_USDT'];
const charts = {};
const chartData = {};

// Initialize data buffers - restore from localStorage if available
SYMBOLS.forEach(symbol => {
    const saved = localStorage.getItem(`chart_${symbol}`);
    if (saved) {
        try {
            chartData[symbol] = JSON.parse(saved);
            console.log(`[Charts] Restored ${chartData[symbol].length} points for ${symbol}`);
        } catch (e) {
            chartData[symbol] = [];
        }
    } else {
        chartData[symbol] = [];
    }
});

// Handle deviation updates from WebSocket
window.handleDeviationUpdate = function (msg) {
    if (!msg.deviations || msg.deviations.length === 0) return;

    msg.deviations.forEach(dev => {
        const symbol = dev.symbol;
        if (!SYMBOLS.includes(symbol)) return;

        // Add data point
        chartData[symbol].push({
            time: Date.now() / 1000,
            value: dev.deviation_pct
        });

        // Keep last 3000 points (5 minutes)
        if (chartData[symbol].length > 3000) {
            chartData[symbol].shift();
        }

        // Save to localStorage
        try {
            localStorage.setItem(`chart_${symbol}`, JSON.stringify(chartData[symbol]));
        } catch (e) { }

        // Update chart if exists
        if (charts[symbol] && chartData[symbol].length > 0) {
            charts[symbol].setData([
                chartData[symbol].map(d => d.time),
                chartData[symbol].map(d => d.value)
            ]);
        }
    });
};

window.handleAllSymbolsScored = function (msg) {
    if (Object.keys(charts).length === 0) {
        createAllCharts();
    }
};

function createAllCharts() {
    const grid = document.getElementById('grid');
    if (!grid) return;

    grid.innerHTML = '';
    grid.style.cssText = 'display: flex; flex-direction: column; gap: 20px; width: 100%; padding: 20px;';

    SYMBOLS.forEach(symbol => {
        const card = document.createElement('div');
        card.style.cssText = 'background:#1a1a1a; border:1px solid #333; border-radius:8px; padding:15px; width:100%;';

        card.innerHTML = `
            <h3 style="margin:0 0 10px 0; color:#aaa; font-size:14px; font-family:monospace;">${symbol}: Binance/MEXC Bid Deviation</h3>
            <div id="chart-${symbol}" style="width:100%; height:200px; background:#0a0a0a;"></div>
        `;

        grid.appendChild(card);

        if (typeof window.uPlot !== 'undefined') {
            const container = document.getElementById(`chart-${symbol}`);

            charts[symbol] = new window.uPlot({
                width: 1800,
                height: 200,

                scales: {
                    x: { time: true },
                    y: {}
                },

                series: [
                    {},
                    {
                        label: "Deviation %",
                        stroke: "#00ff00",
                        width: 1
                    }
                ],

                axes: [
                    {
                        grid: { show: true, stroke: "#222" },
                        ticks: { show: true, stroke: "#aaa" },
                        stroke: "#aaa"
                    },
                    {
                        size: 50,
                        values: (u, vals) => vals.map(v => v.toFixed(4) + '%'),
                        grid: { stroke: "#444", width: 1 },
                        ticks: { show: true, stroke: "#aaa" },
                        stroke: "#aaa"
                    }
                ]
            }, [[], []], container);

            // Load saved data immediately
            if (chartData[symbol].length > 0) {
                charts[symbol].setData([
                    chartData[symbol].map(d => d.time),
                    chartData[symbol].map(d => d.value)
                ]);
                console.log(`[Chart] Loaded ${symbol} with ${chartData[symbol].length} points`);
            }
        }
    });
}

document.addEventListener('DOMContentLoaded', () => {
    console.log('[Charts] DOM ready');
});
