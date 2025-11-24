// MEXC Trades Viewer - Zen Design with ApexCharts
// Architecture: WebSocket → In-memory state → ApexCharts

class MexcTradesViewer {
    constructor() {
        this.ws = null;
        this.symbols = [];
        this.currentPage = 1;
        this.pageSize = 100;
        this.charts = new Map();
        this.reconnectInterval = null;
        this.reconnectDelay = 3000;
        this.init();
    }

    init() {
        this.connectWebSocket();
        this.setupPaginationControls();
        this.setupAutoReconnect();
    }

    connectWebSocket() {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        const host = window.location.hostname;
        const port = '8181';
        this.ws = new WebSocket(`${protocol}//${host}:${port}`);

        this.ws.onopen = () => {
            console.log('[WS] Connected');
            this.updateConnectionStatus(true);
            this.clearReconnect();
            document.getElementById('loading').style.display = 'none';
        };

        this.ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                this.handleMessage(message);
            } catch (err) {
                console.error('[WS] Parse error:', err);
            }
        };

        this.ws.onclose = () => {
            this.updateConnectionStatus(false);
            this.scheduleReconnect();
        };

        this.ws.onerror = (error) => console.error('[WS] Error:', error);
    }

    handleMessage(message) {
        switch (message.type) {
            case 'symbols_metadata':
                this.handleSymbolsMetadata(message);
                break;
            case 'page_data':
                this.handlePageData(message);
                break;
            case 'trade_update':
                this.handleTradeUpdate(message);
                break;
        }
    }

    handleSymbolsMetadata(message) {
        this.symbols = message.symbols || [];
        const totalSymbols = message.total_symbols || this.symbols.length;
        const totalPages = Math.ceil(totalSymbols / this.pageSize);

        document.getElementById('total-symbols').textContent = totalSymbols;
        document.getElementById('total-pages').textContent = totalPages;

        this.subscribeToPage(1);
    }

    handlePageData(message) {
        this.renderChartsForPage(message);
    }

    handleTradeUpdate(message) {
        const symbol = message.symbol;
        const trades = message.trades || [];

        if (!this.charts.has(symbol)) return;

        const chartData = this.charts.get(symbol);

        // Add valid trades to chart
        for (const trade of trades) {
            if (!trade || !trade.timestamp || !trade.price) continue;

            const timestamp = new Date(trade.timestamp).getTime();
            const price = parseFloat(trade.price);

            if (isNaN(timestamp) || isNaN(price) || !isFinite(price) || price <= 0) {
                continue;
            }

            try {
                chartData.chart.appendData([{
                    data: [{ x: timestamp, y: price }]
                }]);
            } catch (err) {
                console.error(`[Chart] Update failed for ${symbol}:`, err);
            }
        }
    }

    subscribeToPage(page) {
        if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;

        this.currentPage = page;
        this.updatePageDisplay();

        this.ws.send(JSON.stringify({
            action: 'subscribe_page',
            page: page,
            page_size: this.pageSize
        }));
    }

    renderChartsForPage(pageData) {
        const grid = document.getElementById('charts-grid');
        grid.innerHTML = '';
        this.charts.clear();

        const symbolsOnPage = pageData.symbols || [];

        for (const symbolData of symbolsOnPage) {
            this.renderChart(symbolData);
        }

        document.getElementById('active-charts').textContent = symbolsOnPage.length;
    }

    renderChart(symbolData) {
        const symbol = symbolData.symbol;
        const trades = symbolData.trades || [];

        const card = document.createElement('div');
        card.className = 'card';
        card.innerHTML = `
            <div class="card-header">
                <div class="symbol-group">
                    <span class="symbol">${this.extractSymbolName(symbol)}</span>
                    <span class="tick-size">±0.0001</span>
                </div>
                <div class="trades-per-min" id="tpm-${symbol}">--/1m</div>
            </div>
            <div class="chart-area" id="chart-${symbol}"></div>
        `;

        document.getElementById('charts-grid').appendChild(card);

        requestAnimationFrame(() => {
            const container = document.getElementById(`chart-${symbol}`);
            if (!container) return;

            // Prepare data
            const validData = trades
                .filter(t => t && t.timestamp && t.price)
                .map(t => ({
                    x: new Date(t.timestamp).getTime(),
                    y: parseFloat(t.price)
                }))
                .filter(point =>
                    !isNaN(point.x) && !isNaN(point.y) &&
                    isFinite(point.x) && isFinite(point.y) && point.y > 0
                )
                .sort((a, b) => a.x - b.x);

            const options = {
                chart: {
                    type: 'line',
                    height: 200,
                    animations: { enabled: false },
                    toolbar: { show: false },
                    zoom: { enabled: false },
                    background: '#0e1014',
                },
                theme: {
                    mode: 'dark',
                },
                series: [{
                    name: 'Price',
                    data: validData
                }],
                xaxis: {
                    type: 'datetime',
                    labels: {
                        style: {
                            colors: '#4a5568',
                            fontSize: '10px'
                        }
                    }
                },
                yaxis: {
                    labels: {
                        style: {
                            colors: '#4a5568',
                            fontSize: '10px'
                        },
                        formatter: (val) => val ? val.toFixed(8).replace(/\.?0+$/, '') : ''
                    }
                },
                grid: {
                    borderColor: 'rgba(255,255,255,0.02)',
                    strokeDashArray: 0,
                },
                stroke: {
                    curve: 'straight',
                    width: 2,
                    colors: ['#00f2ff']
                },
                tooltip: {
                    enabled: true,
                    theme: 'dark',
                    x: { format: 'HH:mm:ss' }
                },
                legend: { show: false },
            };

            const chart = new ApexCharts(container, options);
            chart.render();

            this.charts.set(symbol, { chart, data: validData });
        });
    }

    extractSymbolName(fullSymbol) {
        const parts = fullSymbol.split('_');
        let symbol = parts[parts.length - 1];
        return symbol.replace(/USDT$|USDC$/, '');
    }

    setupPaginationControls() {
        document.getElementById('btn-prev').addEventListener('click', () => this.goToPage(this.currentPage - 1));
        document.getElementById('btn-next').addEventListener('click', () => this.goToPage(this.currentPage + 1));
        document.getElementById('btn-first-bottom').addEventListener('click', () => this.goToPage(1));
        document.getElementById('btn-prev-bottom').addEventListener('click', () => this.goToPage(this.currentPage - 1));
        document.getElementById('btn-next-bottom').addEventListener('click', () => this.goToPage(this.currentPage + 1));
        document.getElementById('btn-last-bottom').addEventListener('click', () => this.goToLastPage());
    }

    goToPage(page) {
        const totalPages = parseInt(document.getElementById('total-pages').textContent) || 1;
        if (page < 1 || page > totalPages) return;
        this.subscribeToPage(page);
    }

    goToLastPage() {
        const totalPages = parseInt(document.getElementById('total-pages').textContent) || 1;
        this.goToPage(totalPages);
    }

    updatePageDisplay() {
        const totalPages = parseInt(document.getElementById('total-pages').textContent) || 1;
        const isFirstPage = this.currentPage === 1;
        const isLastPage = this.currentPage >= totalPages;

        document.getElementById('current-page').textContent = this.currentPage;
        document.getElementById('current-page-bottom').textContent = this.currentPage;

        document.getElementById('btn-prev').disabled = isFirstPage;
        document.getElementById('btn-prev-bottom').disabled = isFirstPage;
        document.getElementById('btn-first-bottom').disabled = isFirstPage;

        document.getElementById('btn-next').disabled = isLastPage;
        document.getElementById('btn-next-bottom').disabled = isLastPage;
        document.getElementById('btn-last-bottom').disabled = isLastPage;
    }

    updateConnectionStatus(connected) {
        const statusDot = document.getElementById('connection-status-dot');
        statusDot.className = connected ? 'status-dot connected' : 'status-dot disconnected';
    }

    scheduleReconnect() {
        if (this.reconnectInterval) return;
        this.reconnectInterval = setTimeout(() => {
            this.reconnectInterval = null;
            this.connectWebSocket();
        }, this.reconnectDelay);
    }

    clearReconnect() {
        if (this.reconnectInterval) {
            clearTimeout(this.reconnectInterval);
            this.reconnectInterval = null;
        }
    }

    setupAutoReconnect() {
        setInterval(() => {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
                this.scheduleReconnect();
            }
        }, 10000);
    }
}

document.addEventListener('DOMContentLoaded', () => {
    window.viewer = new MexcTradesViewer();
});
