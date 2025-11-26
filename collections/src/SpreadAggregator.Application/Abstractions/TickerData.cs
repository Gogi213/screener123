namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// A Data Transfer Object for ticker information, including volume and 24h metrics.
/// </summary>
public class TickerData
{
    public required string Symbol { get; init; }
    public decimal QuoteVolume { get; init; }
    public decimal BestBid { get; init; }
    public decimal BestAsk { get; init; }
    public DateTime Timestamp { get; set; }

    // SPRINT-10: 24h metrics for table view
    public decimal Volume24h { get; init; }
    public decimal PriceChangePercent24h { get; init; }
    public decimal LastPrice { get; init; }
}