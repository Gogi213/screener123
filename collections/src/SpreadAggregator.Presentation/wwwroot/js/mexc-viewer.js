// MEXC Trades Viewer - Real-time trade visualization with pagination
// Architecture: WebSocket → In-memory state → Lightweight Charts

class MexcTradesViewer {
    constructor() {
        this.ws = null;
        this.symbols = [];
        this.currentPage = 1;
        this.pageSize = 100;
        this.charts = new Map(); // symbol → { chart, series, data }
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
        const port = '8181'; // Backend WebSocket port (from appsettings.json)
        this.ws = new WebSocket(`${protocol}//${host}:${port}`);

        this.ws.onopen = () => {
            console.log('[WS] Connected to MEXC trades server');
            this.updateConnectionStatus(true);
            this.clearReconnect();
        };

        this.ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                this.handleMessage(message);
            } catch (err) {
                console.error('[WS] Error parsing message:', err);
            }
        };

        this.ws.onclose = () => {
            console.log('[WS] Disconnected from server');
            this.updateConnectionStatus(false);
            this.scheduleReconnect();
        };

        this.ws.onerror = (error) => {
            console.error('[WS] WebSocket error:', error);
        };
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
            default:
                console.log('[WS] Unknown message type:', message.type);
        }
    }

    handleSymbolsMetadata(message) {
        this.symbols = message.symbols || [];
        const totalSymbols = message.total_symbols || this.symbols.length;
        const totalPages = Math.ceil(totalSymbols / this.pageSize);

        document.getElementById('total-symbols').textContent = totalSymbols;
        document.getElementById('total-pages').textContent = totalPages;
        document.getElementById('total-pages-bottom').textContent = totalPages;

        console.log(`[Metadata] Received ${this.symbols.length} symbols`);

        // Subscribe to first page
        this.subscribeToPage(1);
    }

    handlePageData(message) {
        console.log(`[PageData] Received data for page ${message.page}`);
        this.renderChartsForPage(message);
    }

    handleTradeUpdate(message) {
        const symbol = message.symbol;
        const trades = message.trades || [];

        if (!this.charts.has(symbol)) {
            // Chart not rendered (different page)
            return;
        }

        const chartData = this.charts.get(symbol);

        // Update latest price display
        if (trades.length > 0) {
            const latestTrade = trades[trades.length - 1];
            const priceElement = document.getElementById(`price-${symbol}`);
            if (priceElement) {
                priceElement.textContent = this.formatPrice(latestTrade.price);
            }
        }

        // Add trades to chart
        for (const trade of trades) {
            const timestamp = new Date(trade.timestamp).getTime() / 1000;
            const point = {
                time: timestamp,
                value: parseFloat(trade.price)
            };

            chartData.series.update(point);
        }
    }

    subscribeToPage(page) {
        if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
            console.warn('[WS] Cannot subscribe: not connected');
            return;
        }

        this.currentPage = page;
        this.updatePageDisplay();

        const request = {
            action: 'subscribe_page',
            page: page,
            page_size: this.pageSize
        };

        this.ws.send(JSON.stringify(request));
        console.log(`[WS] Subscribed to page ${page}`);
    }

    renderChartsForPage(pageData) {
        const grid = document.getElementById('charts-grid');
        grid.innerHTML = ''; // Clear existing charts
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

        // Create card element
        const card = document.createElement('div');
        card.className = 'chart-card';
        card.innerHTML = `
            <div class="chart-header">
                <div class="symbol-name">${this.extractSymbolName(symbol)}</div>
                <div class="tick-size">Tick: ${this.formatTickSize(symbol)}</div>
            </div>
            <div class="price-display" id="price-${symbol}">--</div>
            <div class="chart-container" id="chart-${symbol}"></div>
        `;

        document.getElementById('charts-grid').appendChild(card);

        // BUGFIX: Wait for DOM to render before getting container size
        requestAnimationFrame(() => {
            const container = document.getElementById(`chart-${symbol}`);
            if (!container) return;

            const containerWidth = container.clientWidth || 300;


            // Initialize lightweight chart with actual container width
            const chart = LightweightCharts.createChart(container, {
                width: containerWidth,
                height: 280,
                layout: {
                    background: { color: '#0a0e27' },
                    textColor: '#6b7a99',
                },
                grid: {
                    vertLines: { color: '#1f2541' },
                    horzLines: { color: '#1f2541' },
                },
                timeScale: {
                    timeVisible: true,
                    secondsVisible: true,
                    borderColor: '#2a3150',
                },
                rightPriceScale: {
                    borderColor: '#2a3150',
                    scaleMargins: {
                        top: 0.1,
                        bottom: 0.1,
                    },
                },
            });

            const series = chart.addSeries(LightweightCharts.LineSeries, {
                color: '#00d4aa',
                lineWidth: 2,
            });

            // Add historical trades
            if (trades.length > 0) {
                const data = trades.map(t => ({
                    time: new Date(t.timestamp).getTime() / 1000,
                    value: parseFloat(t.price)
                })).sort((a, b) => a.time - b.time);

                series.setData(data);

                // Update price display
                const latestPrice = data[data.length - 1].value;
                document.getElementById(`price-${symbol}`).textContent = this.formatPrice(latestPrice);
            } else {
                // No trades yet - show placeholder
                document.getElementById(`price-${symbol}`).textContent = 'Waiting for trades...';
            }

            // Store chart reference
            this.charts.set(symbol, { chart, series, data: [] });

            // Handle window resize
            window.addEventListener('resize', () => {
                const newWidth = container.clientWidth || 300;
                chart.resize(newWidth, 180);
            });
        });
    }

    extractSymbolName(fullSymbol) {
        // "MEXC_BTCUSDT" → "BTC/USDT"
        const parts = fullSymbol.split('_');
        const symbol = parts[parts.length - 1];

        if (symbol.endsWith('USDT')) {
            return symbol.replace('USDT', '/USDT');
        } else if (symbol.endsWith('USDC')) {
            return symbol.replace('USDC', '/USDC');
        }

        return symbol;
    }

    formatTickSize(symbol) {
        // Placeholder - tick size should come from backend metadata
        // For now, estimate based on price magnitude
        return '0.0001';
    }

    /**
     * Format price with zero compression: 0.00000123 → 0,(5)123
     */
    formatPrice(price) {
        if (typeof price !== 'number') {
            price = parseFloat(price);
        }

        const str = price.toString();

        // Match pattern: 0.000...123
        const match = str.match(/^(0\.)0+/);

        if (!match) {
            // No leading zeros after decimal
            return price.toFixed(8).replace(/\.?0+$/, '');
        }

        const leadingZeros = match[0].length - 2; // Subtract "0."
        const significantDigits = str.slice(match[0].length);

        return `0,(${leadingZeros})${significantDigits}`;
    }

    setupPaginationControls() {
        // Top pagination
        document.getElementById('btn-first').addEventListener('click', () => this.goToPage(1));
        document.getElementById('btn-prev').addEventListener('click', () => this.goToPage(this.currentPage - 1));
        document.getElementById('btn-next').addEventListener('click', () => this.goToPage(this.currentPage + 1));
        document.getElementById('btn-last').addEventListener('click', () => this.goToLastPage());

        // Bottom pagination
        document.getElementById('btn-first-bottom').addEventListener('click', () => this.goToPage(1));
        document.getElementById('btn-prev-bottom').addEventListener('click', () => this.goToPage(this.currentPage - 1));
        document.getElementById('btn-next-bottom').addEventListener('click', () => this.goToPage(this.currentPage + 1));
        document.getElementById('btn-last-bottom').addEventListener('click', () => this.goToLastPage());
    }

    goToPage(page) {
        const totalPages = this.getTotalPages();

        if (page < 1 || page > totalPages) {
            return;
        }

        this.subscribeToPage(page);
    }

    goToLastPage() {
        const totalPages = this.getTotalPages();
        this.goToPage(totalPages);
    }

    getTotalPages() {
        return parseInt(document.getElementById('total-pages').textContent) || 1;
    }

    updatePageDisplay() {
        const totalPages = this.getTotalPages();

        // Update page numbers
        document.getElementById('current-page').textContent = this.currentPage;
        document.getElementById('current-page-bottom').textContent = this.currentPage;
        document.getElementById('current-page-stat').textContent = this.currentPage;

        // Update button states
        const isFirstPage = this.currentPage === 1;
        const isLastPage = this.currentPage >= totalPages;

        document.getElementById('btn-first').disabled = isFirstPage;
        document.getElementById('btn-prev').disabled = isFirstPage;
        document.getElementById('btn-first-bottom').disabled = isFirstPage;
        document.getElementById('btn-prev-bottom').disabled = isFirstPage;

        document.getElementById('btn-next').disabled = isLastPage;
        document.getElementById('btn-last').disabled = isLastPage;
        document.getElementById('btn-next-bottom').disabled = isLastPage;
        document.getElementById('btn-last-bottom').disabled = isLastPage;
    }

    updateConnectionStatus(connected) {
        const statusEl = document.getElementById('connection-status');
        if (connected) {
            statusEl.textContent = 'Connected';
            statusEl.className = 'connection-status status-connected';
        } else {
            statusEl.textContent = 'Disconnected';
            statusEl.className = 'connection-status status-disconnected';
        }
    }

    scheduleReconnect() {
        if (this.reconnectInterval) return;

        console.log(`[WS] Reconnecting in ${this.reconnectDelay}ms...`);
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
        // Monitor connection health
        setInterval(() => {
            if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
                this.scheduleReconnect();
            }
        }, 10000); // Check every 10 seconds
    }
}

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => {
    window.viewer = new MexcTradesViewer();
});
