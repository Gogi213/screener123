using System;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Static calculator for price deviation between exchanges.
/// 
/// Implements deviation formula from analyzer (deviation from price parity, not mean).
/// Critical: deviation = 0 means prices are EQUAL → can close arbitrage position at break-even.
/// 
/// Formula: deviation = ((price1 / price2) - 1.0) * 100
/// 
/// Examples:
///   price1 = 43250.50, price2 = 43151.25
///   ratio = 1.00229...
///   deviation = +0.229% (ex1 is 0.229% more expensive)
///   
///   price1 = 43151.25, price2 = 43250.50
///   ratio = 0.99770...
///   deviation = -0.230% (ex1 is 0.230% cheaper)
///   
///   price1 = 43200.00, price2 = 43200.00
///   ratio = 1.0
///   deviation = 0% (price parity)
/// </summary>
public static class DeviationCalculator
{
    /// <summary>
    /// Calculate deviation from price parity.
    /// 
    /// Analyzer source: analysis.py:104-114
    /// ratio = bid_ex1 / bid_ex2
    /// deviation = (ratio - 1.0) / 1.0 * 100
    /// 
    /// Important: We calculate deviation from 1.0 (price equality), NOT from mean ratio!
    /// This ensures deviation = 0 always means "prices are equal".
    /// </summary>
    /// <param name="price1">Price from first exchange</param>
    /// <param name="price2">Price from second exchange</param>
    /// <returns>
    /// Deviation in percentage:
    ///   0%     → prices equal (parity)
    ///   +0.5%  → ex1 is 0.5% more expensive than ex2
    ///   -0.3%  → ex1 is 0.3% cheaper than ex2
    /// </returns>
    public static decimal CalculateDeviation(decimal price1, decimal price2)
    {
        // Avoid division by zero
        if (price2 == 0)
            return 0;
        
        // Calculate ratio
        var ratio = price1 / price2;
        
        // Deviation from 1.0 (price equality)
        var deviation = (ratio - 1.0m) * 100m;
        
        // Round to 4 decimal places (0.0001% precision)
        return Math.Round(deviation, 4);
    }

    /// <summary>
    /// Check if deviation is within acceptable range for arbitrage.
    /// Typical thresholds: 0.2% - 0.5%
    /// </summary>
    public static bool IsSignificantDeviation(decimal deviation, decimal threshold = 0.2m)
    {
        return Math.Abs(deviation) > threshold;
    }

    /// <summary>
    /// Check if deviation is near zero (price parity).
    /// Used to detect opportunity completion (can close position).
    /// </summary>
    public static bool IsNearParity(decimal deviation, decimal neutralThreshold = 0.05m)
    {
        return Math.Abs(deviation) < neutralThreshold;
    }
}
