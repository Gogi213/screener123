using System;

namespace SpreadAggregator.Domain.Entities;

/// <summary>
/// Book Ticker data from WebSocket bookTicker stream.
/// Contains realtime best bid/ask prices (updated every ~100ms or on change).
/// Used for zero-latency bid/bid and ask/ask deviation analysis.
/// </summary>
public class BookTickerData : MarketData
{
    /// <summary>
    /// Best bid price (highest buy price in orderbook)
    /// </summary>
    public required decimal BestBid { get; init; }
    
    /// <summary>
    /// Best ask price (lowest sell price in orderbook)
    /// </summary>
    public required decimal BestAsk { get; init; }
    
    /// <summary>
    /// Quantity available at best bid price
    /// </summary>
    public decimal BestBidQty { get; init; }
    
    /// <summary>
    /// Quantity available at best ask price
    /// </summary>
    public decimal BestAskQty { get; init; }
}
