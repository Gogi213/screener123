// Bid/Bid + LastPrice Deviation Charts - Multi-line visualization
// Green = LastPrice/LastPrice, Blue = Bid/Bid

console.log('[Charts] Initializing dual-line deviation charts...');

const SYMBOLS = ['BTC_USDT', 'ETH_USDT', 'SOL_USDT', 'ZEC_USDT', 'SUI_USDT', 'ASTER_USDT', 'DOGE_USDT', 'HYPE_USDT', 'LINK_USDT'];
const charts = {};
const chartData = {};

// Initialize data buffers (no localStorage - RAM only for performance)
SYMBOLS.forEach(symbol => {
    chartData[symbol] = [];
});

// Handle deviation updates from WebSocket
window.handleDeviationUpdate = function (msg) {
    if (!msg.deviations || msg.deviations.length === 0) return;

    msg.deviations.forEach(dev => {
        const symbol = dev.symbol;
        if (!SYMBOLS.includes(symbol)) return;

        // Add data point with BOTH deviations
        chartData[symbol].push({
            time: Date.now() / 1000,
            lastprice: dev.deviation_lastprice_pct || 0,
            bid: dev.deviation_bid_pct || 0
        });

        // Keep last 1200 points (2 minutes at 100ms interval)
        if (chartData[symbol].length > 1200) {
            chartData[symbol].shift();
        }

        // Update chart if exists
        if (charts[symbol] && chartData[symbol].length > 0) {
            charts[symbol].setData([
                chartData[symbol].map(d => d.time),
                chartData[symbol].map(d => d.lastprice),
                chartData[symbol].map(d => d.bid)
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
            <h3 style="margin:0 0 10px 0; color:#aaa; font-size:14px; font-family:monospace;">
                ${symbol}: Binance/MEXC Deviation
                <span style="color:#00ff00; margin-left:15px;">● LastPrice</span>
                <span style="color:#00aaff; margin-left:10px;">● Bid</span>
            </h3>
            <div id="chart-${symbol}" style="width:100%; height:400px; background:#0a0a0a;"></div>
        `;

        grid.appendChild(card);

        if (typeof window.uPlot !== 'undefined') {
            const container = document.getElementById(`chart-${symbol}`);

            charts[symbol] = new window.uPlot({
                width: 1800,
                height: 400,

                scales: {
                    x: { time: true },
                    y: {}
                },

                series: [
                    {},
                    {
                        label: "LastPrice Deviation %",
                        stroke: "#00ff00",  // Green
                        width: 2
                    },
                    {
                        label: "Bid Deviation %",
                        stroke: "#00aaff",  // Blue
                        width: 2
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
            }, [[], [], []], container);

            console.log(`[Chart] Created dual-line chart for ${symbol}`);
        }
    });
}

document.addEventListener('DOMContentLoaded', () => {
    console.log('[Charts] DOM ready - awaiting data...');
});
