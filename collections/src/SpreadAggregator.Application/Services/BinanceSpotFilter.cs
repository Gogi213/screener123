using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Binance Spot Filter - excludes MEXC symbols that are listed on Binance Spot
/// Use case: Screen for MEXC-exclusive tokens (potential pumps before Binance listing)
/// </summary>
public class BinanceSpotFilter
{
    private HashSet<string> _binanceSpotSymbols = new();
    private readonly object _lock = new();
    private bool _isLoaded = false;

    /// <summary>
    /// Load Binance Spot symbols from public API
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            Console.WriteLine("[Binance Filter] Loading Binance Spot symbols...");
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.GetStringAsync("https://api.binance.com/api/v3/exchangeInfo");
            var json = JsonDocument.Parse(response);
            var symbolsArray = json.RootElement.GetProperty("symbols");
            
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var symbolElement in symbolsArray.EnumerateArray())
            {
                // Only TRADING status symbols
                if (symbolElement.GetProperty("status").GetString() == "TRADING")
                {
                    var symbol = symbolElement.GetProperty("symbol").GetString();
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        symbols.Add(symbol);
                    }
                }
            }
            
            lock (_lock)
            {
                _binanceSpotSymbols = symbols;
                _isLoaded = true;
            }
            
            Console.WriteLine($"[Binance Filter] ✅ Loaded {_binanceSpotSymbols.Count} Binance Spot symbols");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance Filter] ❌ ERROR: {ex.Message}");
            Console.WriteLine($"[Binance Filter] ⚠️  Will proceed WITHOUT Binance filtering");
            
            lock (_lock)
            {
                _isLoaded = false; // Mark as failed - will not filter
            }
        }
    }

    /// <summary>
    /// Check if symbol exists on Binance Spot
    /// </summary>
    public bool IsOnBinance(string symbol)
    {
        if (!_isLoaded)
            return false; // If not loaded, don't filter

        lock (_lock)
        {
            // MEXC format: BTCUSDT
            // Binance format: BTCUSDT (same)
            return _binanceSpotSymbols.Contains(symbol);
        }
    }

    /// <summary>
    /// Filter list of symbols to exclude Binance Spot tokens
    /// </summary>
    public List<string> FilterExcludeBinance(List<string> mexcSymbols)
    {
        if (!_isLoaded)
        {
            Console.WriteLine("[Binance Filter] ⚠️  Not loaded, returning all MEXC symbols");
            return mexcSymbols;
        }

        var originalCount = mexcSymbols.Count;
        var filtered = mexcSymbols.Where(s => !IsOnBinance(s)).ToList();
        var excluded = originalCount - filtered.Count;

        if (excluded > 0)
        {
            Console.WriteLine($"[Binance Filter] 🔍 Excluded {excluded} Binance-listed symbols from {originalCount} MEXC symbols");
            Console.WriteLine($"[Binance Filter] 📊 Result: {filtered.Count} MEXC-exclusive symbols");
        }

        return filtered;
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public (int BinanceCount, bool IsLoaded) GetStats()
    {
        lock (_lock)
        {
            return (_binanceSpotSymbols.Count, _isLoaded);
        }
    }
}
