// MEXC Trades Viewer - Zen Design
// Architecture: WebSocket → In-memory state → Lightweight Charts

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

        // Update price display
        if (trades.length > 0) {
            const latestTrade = trades[trades.length - 1];
            const priceEl = document.getElementById(`price-${symbol}`);
            if (priceEl && latestTrade && latestTrade.price) {
                priceEl.textContent = this.formatPrice(latestTrade.price);
            }
        }

        // Add valid trades to chart
        for (const trade of trades) {
            if (!trade || !trade.timestamp || !trade.price) continue;

            const timestamp = new Date(trade.timestamp).getTime() / 1000;
            const price = parseFloat(trade.price);

            if (isNaN(timestamp) || isNaN(price) || !isFinite(timestamp) || !isFinite(price) || price <= 0) {
                continue;
            }

            try {
                chartData.series.update({ time: timestamp, value: price });
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
                <div class="symbol">${this.extractSymbolName(symbol)}</div>
                <div class="price" id="price-${symbol}">--</div>
            </div>
            <div class="chart-area" id="chart-${symbol}"></div>
        `;

        document.getElementById('charts-grid').appendChild(card);

        requestAnimationFrame(() => {
            const container = document.getElementById(`chart-${symbol}`);
            if (!container) return;

            const chart = LightweightCharts.createChart(container, {
                width: container.clientWidth || 300,
                height: container.clientHeight || 200,
                layout: {
                    background: { color: '#0e1014' },
                    textColor: '#4a5568',
                },
                grid: {
                    vertLines: { color: 'rgba(255,255,255,0.02)' },
                    horzLines: { color: 'rgba(255,255,255,0.02)' },
                },
                timeScale: {
                    timeVisible: true,
                    secondsVisible: true,
                    borderColor: 'rgba(255,255,255,0.05)',
                },
                rightPriceScale: {
                    borderColor: 'rgba(255,255,255,0.05)',
                    scaleMargins: { top: 0.2, bottom: 0.2 },
                },
                crosshair: {
                    vertLine: { color: 'rgba(0,242,255,0.2)', labelBackgroundColor: '#00f2ff' },
                    horzLine: { color: 'rgba(0,242,255,0.2)', labelBackgroundColor: '#00f2ff' },
                },
            });

            const series = chart.addSeries(LightweightCharts.LineSeries, {
                color: '#00f2ff',
                lineWidth: 2,
                crosshairMarkerVisible: true,
                crosshairMarkerRadius: 4,
            });

            // Add historical trades with validation
            if (trades.length > 0) {
                const validData = trades
                    .filter(t => t && t.timestamp && t.price)
                    .map(t => {
                        const time = new Date(t.timestamp).getTime() / 1000;
                        const value = parseFloat(t.price);
                        return { time, value };
                    })
                    .filter(point =>
                        !isNaN(point.time) && !isNaN(point.value) &&
                        isFinite(point.time) && isFinite(point.value) &&
                        point.value > 0
                    )
                    .sort((a, b) => a.time - b.time);

                if (validData.length > 0) {
                    series.setData(validData);
                    const latestPrice = validData[validData.length - 1].value;
                    document.getElementById(`price-${symbol}`).textContent = this.formatPrice(latestPrice);
                } else {
                    document.getElementById(`price-${symbol}`).textContent = '...';
                }
            } else {
                document.getElementById(`price-${symbol}`).textContent = '...';
            }

            this.charts.set(symbol, { chart, series });

            window.addEventListener('resize', () => {
                if (container) {
                    chart.resize(container.clientWidth, container.clientHeight);
                }
            });
        });
    }

    extractSymbolName(fullSymbol) {
        const parts = fullSymbol.split('_');
        let symbol = parts[parts.length - 1];
        return symbol.replace(/USDT$|USDC$/, '');
    }

    formatPrice(price) {
        if (typeof price !== 'number') price = parseFloat(price);
        if (!isFinite(price)) return '--';

        const str = price.toString();
        const match = str.match(/^(0\.)0+/);

        if (!match) {
            return price.toFixed(8).replace(/\.?0+$/, '');
        }

        const leadingZeros = match[0].length - 2;
        const significantDigits = str.slice(match[0].length);
        return `0.0{${leadingZeros}}${significantDigits.substring(0, 4)}`;
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
