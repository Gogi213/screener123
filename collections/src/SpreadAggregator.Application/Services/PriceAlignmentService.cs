using System;
using System.Collections.Concurrent;
using System.Linq;
using SpreadAggregator.Domain.Entities;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Price alignment service for synchronizing bid/ask between exchange pairs.
/// 
/// Implements join_asof logic from analyzer (backward strategy - no look-ahead bias).
/// For any timestamp, finds the last known price from each exchange.
/// 
/// Example:
///   Ex1: [10:00:00, 10:00:05, 10:00:10]
///   Ex2: [10:00:02, 10:00:07, 10:00:12]
///   
///   AlignPrices(targetTime=10:00:08)
///   → Ex1: price at 10:00:05 (last known before 10:00:08)
///   → Ex2: price at 10:00:07 (last known before 10:00:08)
/// </summary>
public class PriceAlignmentService
{
    private readonly ConcurrentDictionary<string, Queue<TradeData>> _symbolTrades;

    public PriceAlignmentService(
        ConcurrentDictionary<string, Queue<TradeData>> symbolTrades)
    {
        _symbolTrades = symbolTrades;
    }

    /// <summary>
    /// Get aligned prices between two exchanges at given timestamp.
    /// Uses backward strategy (no look-ahead bias) - takes last known price ≤ targetTime.
    /// 
    /// Analog of analyzer's: join_asof(data1, data2, on='timestamp')
    /// </summary>
    /// <param name="symbol">Symbol (e.g., "BTC_USDT")</param>
    /// <param name="ex1">First exchange name</param>
    /// <param name="ex2">Second exchange name</param>
    /// <param name="targetTime">Target timestamp for alignment</param>
    /// <returns>Aligned prices or null if data missing</returns>
    public (decimal price1, decimal price2, DateTime alignedTime)? 
        GetAlignedPrices(string symbol, string ex1, string ex2, DateTime targetTime)
    {
        // Get last known price ≤ targetTime for ex1
        var price1 = GetLastPriceBeforeTime(
            $"{ex1}_{symbol}", targetTime);
        
        // Get last known price ≤ targetTime for ex2
        var price2 = GetLastPriceBeforeTime(
            $"{ex2}_{symbol}", targetTime);
        
        // Both prices must exist for valid alignment
        if (!price1.HasValue || !price2.HasValue)
            return null;
        
        // Return aligned prices with target timestamp
        return (price1.Value, price2.Value, targetTime);
    }

    /// <summary>
    /// Get last known price before or at target time (backward lookup).
    /// 
    /// This prevents look-ahead bias - we only use information available
    /// at the target time, not future prices.
    /// </summary>
    /// <param name="symbolKey">Exchange_Symbol key (e.g., "Binance_BTC_USDT")</param>
    /// <param name="targetTime">Target timestamp</param>
    /// <returns>Last price or null if no data</returns>
    private decimal? GetLastPriceBeforeTime(
        string symbolKey, DateTime targetTime)
    {
        if (!_symbolTrades.TryGetValue(symbolKey, out var queue))
            return null;
        
        // Find last trade with timestamp ≤ targetTime
        // OrderByDescending ensures we get the most recent one
        var trade = queue
            .Where(t => t.Timestamp <= targetTime)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefault();
        
        return trade?.Price;
    }

    /// <summary>
    /// Get aligned prices for all available exchange pairs for a symbol.
    /// 
    /// Example:
    ///   Symbol: "BTC_USDT"
    ///   Exchanges: [Binance, MexcFutures, Bybit]
    ///   Returns: [(Binance, MexcFutures), (Binance, Bybit), (MexcFutures, Bybit)]
    /// </summary>
    public System.Collections.Generic.List<(string ex1, string ex2, decimal price1, decimal price2)> 
        GetAllAlignedPairs(string symbol, DateTime targetTime)
    {
        var results = new System.Collections.Generic.List<(string, string, decimal, decimal)>();

        // Find all exchanges that have this symbol
        var exchanges = _symbolTrades.Keys
            .Where(k => k.EndsWith($"_{symbol}"))
            .Select(k => k.Split('_')[0])
            .Distinct()
            .ToList();

        // Generate all pairs (combinations)
        for (int i = 0; i < exchanges.Count; i++)
        {
            for (int j = i + 1; j < exchanges.Count; j++)
            {
                var ex1 = exchanges[i];
                var ex2 = exchanges[j];

                var aligned = GetAlignedPrices(symbol, ex1, ex2, targetTime);
                if (aligned.HasValue)
                {
                    results.Add((ex1, ex2, aligned.Value.price1, aligned.Value.price2));
                }
            }
        }

        return results;
    }
}
