using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Major Exchanges Filter - excludes MEXC symbols that are listed on major exchanges (Binance, Bybit, OKX)
/// Use case: Screen for MEXC-exclusive tokens (potential pumps before major exchange listing)
/// </summary>
public class BinanceSpotFilter // Keep old name for compatibility
{
    private HashSet<string> _allSymbols = new();
    private readonly object _lock = new();
    private bool _isLoaded = false;

    /// <summary>
    /// Load symbols from Binance, Bybit, OKX public APIs
    /// </summary>
    public async Task LoadAsync()
    {
        var allSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load from each exchange
        await LoadBinance(allSymbols);
        await LoadBybit(allSymbols);
        await LoadOKX(allSymbols);

        lock (_lock)
        {
            _allSymbols = allSymbols;
            _isLoaded = allSymbols.Count > 0;
        }

        if (_isLoaded)
        {
            Console.WriteLine($"[Major Exchanges Filter] ✅ Loaded {_allSymbols.Count} total symbols from Binance + Bybit + OKX");
        }
        else
        {
            Console.WriteLine($"[Major Exchanges Filter] ⚠️  Failed to load any symbols, will NOT filter");
        }
    }

    private async Task LoadBinance(HashSet<string> symbols)
    {
        try
        {
            Console.WriteLine("[Binance] Loading Spot symbols...");
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.GetStringAsync("https://api.binance.com/api/v3/exchangeInfo");
            var json = JsonDocument.Parse(response);
            var symbolsArray = json.RootElement.GetProperty("symbols");
            
            var count = 0;
            foreach (var symbolElement in symbolsArray.EnumerateArray())
            {
                if (symbolElement.GetProperty("status").GetString() == "TRADING")
                {
                    var symbol = symbolElement.GetProperty("symbol").GetString();
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        symbols.Add(symbol);
                        count++;
                    }
                }
            }
            
            Console.WriteLine($"[Binance] ✅ Loaded {count} symbols");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Binance] ❌ ERROR: {ex.Message}");
        }
    }

    private async Task LoadBybit(HashSet<string> symbols)
    {
        try
        {
            Console.WriteLine("[Bybit] Loading Spot symbols...");
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            // Bybit API v5: /v5/market/instruments-info?category=spot
            var response = await httpClient.GetStringAsync("https://api.bybit.com/v5/market/instruments-info?category=spot");
            var json = JsonDocument.Parse(response);
            
            if (json.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("list", out var list))
            {
                var count = 0;
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("symbol", out var symbolProp) &&
                        item.TryGetProperty("status", out var statusProp))
                    {
                        var symbol = symbolProp.GetString();
                        var status = statusProp.GetString();
                        
                        if (!string.IsNullOrEmpty(symbol) && status == "Trading")
                        {
                            symbols.Add(symbol);
                            count++;
                        }
                    }
                }
                
                Console.WriteLine($"[Bybit] ✅ Loaded {count} symbols");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bybit] ❌ ERROR: {ex.Message}");
        }
    }

    private async Task LoadOKX(HashSet<string> symbols)
    {
        try
        {
            Console.WriteLine("[OKX] Loading Spot symbols...");
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            // OKX API: /api/v5/public/instruments?instType=SPOT
            var response = await httpClient.GetStringAsync("https://www.okx.com/api/v5/public/instruments?instType=SPOT");
            var json = JsonDocument.Parse(response);
            
            if (json.RootElement.TryGetProperty("data", out var data))
            {
                var count = 0;
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("instId", out var instIdProp) &&
                        item.TryGetProperty("state", out var stateProp))
                    {
                        var instId = instIdProp.GetString(); // Format: BTC-USDT
                        var state = stateProp.GetString();
                        
                        if (!string.IsNullOrEmpty(instId) && state == "live")
                        {
                            // Convert BTC-USDT → BTCUSDT (remove dash)
                            var symbol = instId.Replace("-", "");
                            symbols.Add(symbol);
                            count++;
                        }
                    }
                }
                
                Console.WriteLine($"[OKX] ✅ Loaded {count} symbols");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OKX] ❌ ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if symbol exists on any major exchange
    /// </summary>
    public bool IsOnBinance(string symbol)
    {
        if (!_isLoaded)
            return false;

        lock (_lock)
        {
            return _allSymbols.Contains(symbol);
        }
    }

    /// <summary>
    /// Filter list of symbols to exclude major exchange tokens
    /// </summary>
    public List<string> FilterExcludeBinance(List<string> mexcSymbols)
    {
        if (!_isLoaded)
        {
            Console.WriteLine("[Major Exchanges Filter] ⚠️  Not loaded, returning all MEXC symbols");
            return mexcSymbols;
        }

        var originalCount = mexcSymbols.Count;
        var filtered = mexcSymbols.Where(s => !IsOnBinance(s)).ToList();
        var excluded = originalCount - filtered.Count;

        if (excluded > 0)
        {
            Console.WriteLine($"[Major Exchanges Filter] 🔍 Excluded {excluded} major-exchange symbols from {originalCount} MEXC symbols");
            Console.WriteLine($"[Major Exchanges Filter] 📊 Result: {filtered.Count} MEXC-exclusive symbols");
        }

        return filtered;
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public (int TotalCount, bool IsLoaded) GetStats()
    {
        lock (_lock)
        {
            return (_allSymbols.Count, _isLoaded);
        }
    }
}
