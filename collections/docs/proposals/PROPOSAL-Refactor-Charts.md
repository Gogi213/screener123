# PROPOSAL: Refactor Charts for Raw Price Visualization

**Date:** 2025-12-03

**Status:** Proposed

## 1. Overview

This proposal outlines the necessary changes to refactor the charting functionality of the `collections` project. The goal is to move from displaying price *deviations* to displaying raw, aligned prices from multiple exchanges, as per the user's vision.

The user's vision is to have a chart that displays three types of real-time data for whitelisted coins:

1.  Last Price / Last Price
2.  Bid Price / Bid Price
3.  Ask Price / Ask Price

This will provide a direct comparison of the prices on different exchanges, rather than an abstract deviation percentage.

## 2. Proposed Changes

The implementation will require changes to both the backend and the frontend.

### 2.1. Backend Changes

The backend will be modified to send raw, aligned prices to the frontend.

#### 2.1.1. Create a new `RealtimePriceService`

A new service, `RealtimePriceService`, will be created in `SpreadAggregator.Application`. This service will be responsible for fetching aligned prices and broadcasting them.

-   The service will use the existing `PriceAlignmentService` to get the aligned prices.
-   It will fetch **last prices**, **bid prices**, and **ask prices**. The `PriceAlignmentService` already has access to this data. A new method, `GetAlignedAskPrices`, will be added to it, similar to the existing `GetAlignedBidPrices`.
-   The service will run on a periodic timer, similar to `DeviationAnalysisService`.

#### 2.1.2. Define a new WebSocket message

A new WebSocket message, `raw_price_update`, will be defined. This message will contain the raw prices for a given symbol from two exchanges.

**Example Message:**

```json
{
  "type": "raw_price_update",
  "symbol": "BTC_USDT",
  "exchange1": "Binance",
  "exchange2": "MexcFutures",
  "timestamp": 1672531200000,
  "lastPrice1": 50000.10,
  "lastPrice2": 50000.05,
  "bid1": 50000.05,
  "bid2": 50000.00,
  "ask1": 50000.15,
  "ask2": 50000.20
}
```

#### 2.1.3. Register the new service

The `RealtimePriceService` will be registered in the application's service container and started alongside the other services in `Program.cs` or the application host.

### 2.2. Frontend Changes

The frontend JavaScript code will be updated to consume the new WebSocket message and render the charts as required.

#### 2.2.1. Update `screener.js`

The `/js/screener.js` file will be modified to handle the `raw_price_update` message.

-   The WebSocket `onmessage` handler in `index.html` will be updated to pass the new message to a handler function in `screener.js`.
-   The charting logic will be updated to store and process the raw price data.

#### 2.2.2. Update `uPlot` Configuration

The `uPlot` chart configuration will be changed to display the raw prices.

-   The chart will now have 6 series (or 3 pairs of series) to display the prices from the two exchanges.
-   The series will be:
    1.  `Last Price (Exchange 1)`
    2.  `Last Price (Exchange 2)`
    3.  `Bid Price (Exchange 1)`
    4.  `Bid Price (Exchange 2)`
    5.  `Ask Price (Exchange 1)`
    6.  `Ask Price (Exchange 2)`
-   The series will be styled with different colors to distinguish them, as per the user's request. For example:
    -   Last Prices: Green / Light Green
    -   Bid Prices: Blue / Light Blue
    -   Ask Prices: Red / Light Red

-   The Y-axis will be updated to display the price values (e.g., in USD) instead of a percentage.

## 3. Implementation Plan

1.  **Backend**:
    1.  Add `GetAlignedAskPrices` to `PriceAlignmentService`.
    2.  Implement `RealtimePriceService`.
    3.  Add the new service to the application host.
2.  **Frontend**:
    1.  Update `index.html` to handle the `raw_price_update` message.
    2.  Update `screener.js` to process and store the new data.
    3.  Reconfigure `uPlot` to display the 6 new series.
3.  **Testing**:
    1.  Verify that the backend sends the correct data.
    2.  Verify that the frontend displays the charts correctly with the real-time data.

This refactoring will align the application with the user's vision and provide a more intuitive and useful charting feature.
