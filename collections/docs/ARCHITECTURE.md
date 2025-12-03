# Architecture: SpreadAggregator

This document provides a comprehensive technical overview of the `SpreadAggregator` system.

## 1. System Overview

**SpreadAggregator** is a high-performance .NET application designed to monitor cryptocurrency prices across multiple exchanges in real-time. Its primary goal is to detect and visualize arbitrage opportunities (price deviations) for a specific **whitelist** of assets.

### Core Vision (The "Deep" View)

The system is evolving towards a unified visualization that displays three distinct types of price deviations simultaneously on a single graph:
1.  **LastPrice / LastPrice**: Deviation between the most recent trade prices.
2.  **Bid / Bid**: Deviation between the best bid prices (order book top).
3.  **Ask / Ask**: Deviation between the best ask prices (order book top).

**Key Constraints:**
-   **Data Source**: Must use **Trades** (individual trade execution events) for the LastPrice metric, not aggregated candles (AggTrades).
-   **Real-time**: Latency must be minimized.
-   **Whitelist**: Only specific, high-interest symbols are monitored (e.g., BTC, ETH, SOL, DOGE).

---

## 2. Architecture & Data Flow

```mermaid
graph TD
    subgraph "Infrastructure Layer"
        Binance[Binance Futures API] -->|WebSocket (Trades & BookTicker)| NativeWS[BinanceFuturesNativeWebSocketClient]
        Mexc[MEXC Futures API] -->|HTTP Polling| MexcClient[MexcFuturesExchangeClient]
        NativeWS -->|TradeData| ExchangeClient[BinanceFuturesExchangeClient]
    end

    subgraph "Application Layer"
        ExchangeClient -->|Channel<MarketData>| Orchestrator[OrchestrationService]
        Orchestrator -->|Channel<MarketData>| Aggregator[TradeAggregatorService]
        
        subgraph "Services"
            Aggregator -- Stores & Processes --> TradeQueue[(Trade Queue)]
            Alignment[PriceAlignmentService] -- Syncs Prices --> Deviation[DeviationAnalysisService]
        end
        
        Deviation -- Calculates % --> WebSocketServer[WebSocket Server :8181]
    end

    subgraph "Presentation Layer"
        WebSocketServer -->|JSON Updates| Frontend[Browser (screener.js)]
    end
```

### Data Flow Steps
1.  **Ingestion**: `BinanceFuturesNativeWebSocketClient` connects to Binance's raw WebSocket stream to receive individual trades and book ticker updates.
2.  **Normalization**: `BinanceFuturesExchangeClient` normalizes symbols (e.g., `BTCUSDT` -> `BTC_USDT`) and data structures.
3.  **Orchestration**: `OrchestrationService` manages the lifecycle of exchange clients and routes data to the aggregator.
4.  **Aggregation**: `TradeAggregatorService` maintains a rolling window of trades (5 minutes) and calculates metrics like volume, acceleration, and imbalance.
5.  **Alignment & Analysis**: `DeviationAnalysisService` aligns prices from different exchanges by time and calculates the percentage difference.
6.  **Broadcast**: Updates are pushed to the frontend via WebSocket.

---

## 3. Component Deep Dive

### 3.1 OrchestrationService
-   **Role**: System manager. Starts/stops exchange clients, manages the global symbol list, and applies filters.
-   **Key Features**:
    -   **Volume Filter**: Excludes symbols with 24h volume below a threshold (configured in `appsettings.json`).
    -   **Blacklist**: Hardcoded exclusion of certain base assets (e.g., XRP, BNB).
    -   **BookTicker Subscription**: Uses `IBookTickerProvider` to subscribe to real-time bid/ask updates if the client supports it.

### 3.2 TradeAggregatorService
-   **Role**: The "Brain". Processes the stream of trades.
-   **Key Features**:
    -   **Rolling Window**: Keeps trades in memory for 5 minutes to calculate moving averages and acceleration.
    -   **Batching**: Accumulates trades and broadcasts them in chunks (every 200ms) to reduce network load.
    -   **Metrics**: Calculates:
        -   `Acceleration`: Rate of change in trade frequency.
        -   `Imbalance`: Buy vs. Sell volume ratio.
        -   `PumpScore`: Heuristic for detecting pumps.

### 3.3 DeviationAnalysisService
-   **Role**: The "Analyst".
-   **Frequency**: Runs every 100ms.
-   **Logic**:
    -   Matches the latest price from Exchange A with the latest price from Exchange B for the same symbol.
    -   Calculates deviation: `((Price1 - Price2) / Price2) * 100`.
    -   **Gap**: Currently calculates LastPrice and Bid deviations. Ask deviation is planned but not implemented.

### 3.4 BinanceFuturesNativeWebSocketClient
-   **Role**: High-performance socket client.
-   **Implementation**: Uses `System.Net.WebSockets.ClientWebSocket` directly (no third-party libraries) for minimal overhead.
-   **Resilience**: Handles reconnection automatically.

---

## 4. Data Structures

### TradeData
Represents a single executed trade.
```csharp
public class TradeData : MarketData
{
    public string Symbol { get; set; }      // e.g., "BTC_USDT"
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public string Side { get; set; }        // "Buy" or "Sell"
    public DateTime Timestamp { get; set; }
}
```

### DeviationData
The payload sent to the frontend for charting.
```csharp
internal class DeviationData
{
    public string Symbol { get; set; }
    public decimal Deviation { get; set; }      // LastPrice Deviation %
    public decimal? DeviationBid { get; set; }  // Bid Deviation %
    public decimal Price1 { get; set; }
    public decimal Price2 { get; set; }
    public decimal? Bid1 { get; set; }
    public decimal? Bid2 { get; set; }
}
```

---

## 5. API Reference (WebSocket)

The WebSocket server (port 8181) broadcasts the following message types:

### 5.1 `deviation_update`
Sent every ~100ms. Contains the latest deviation points for charts.
```json
{
  "type": "deviation_update",
  "timestamp": 1678900000123,
  "deviations": [
    {
      "symbol": "BTC_USDT",
      "exchange1": "Binance",
      "exchange2": "Mexc",
      "deviation_lastprice_pct": 0.15,
      "deviation_bid_pct": 0.12,
      "price1": 25000.00,
      "price2": 24962.50
    }
  ]
}
```

### 5.2 `trade_aggregate`
Sent every ~200ms. Contains aggregated OHLCV data for the last chunk of trades.
```json
{
  "type": "trade_aggregate",
  "symbol": "BTC_USDT",
  "aggregate": {
    "open": 25000.00,
    "high": 25005.00,
    "low": 24995.00,
    "close": 25002.00,
    "volume": 150000.00,
    "buyVolume": 80000.00,
    "sellVolume": 70000.00
  }
}
```

### 5.3 `all_symbols_scored`
Sent periodically (every ~2s). Contains the full list of symbols with their calculated scores and metrics (for the table view).
```json
{
  "type": "all_symbols_scored",
  "symbols": [
    {
      "symbol": "BTC_USDT",
      "score": 12.5,
      "acceleration": 1.2,
      "imbalance": 0.3,
      "volume24h": 500000000
    }
  ]
}
```
