using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Binance Futures exchange client with hardcoded whitelist of 9 symbols.
/// REST API: HttpClient + JSON for symbols/tickers
/// WebSocket: BinanceFuturesNativeWebSocketClient for real-time trades
/// </summary>
public class BinanceFuturesExchangeClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private BinanceFuturesNativeWebSocketClient? _nativeWebSocket;

    private const string BASE_URL = "https://fapi.binance.com";

    // HARDCODED WHITELIST: Normalized symbols with underscores (unified format)
    private static readonly HashSet<string> WHITELISTED_SYMBOLS = new()
    {
        "BTC_USDT", "ETH_USDT", "SOL_USDT", "ZEC_USDT", "SUI_USDT",
        "ASTER_USDT", "DOGE_USDT", "HYPE_USDT", "LINK_USDT"
    };

    public string ExchangeName => "Binance";

    /// <summary>
    /// Normalize Binance symbol format to unified format with underscores
    /// BTCUSDT → BTC_USDT
    /// </summary>
    private static string NormalizeSymbol(string binanceSymbol)
    {
        var quoteCurrencies = new[] { "USDT", "BUSD", "USDC" };
        
        foreach (var quote in quoteCurrencies)
        {
            if (binanceSymbol.EndsWith(quote))
            {
                var baseAsset = binanceSymbol.Substring(0, binanceSymbol.Length - quote.Length);
                return $"{baseAsset}_{quote}";
            }
        }
        
        return binanceSymbol; // No normalization needed
    }

    /// <summary>
    /// Denormalize symbol for Binance API (remove underscores)
    /// BTC_USDT → BTCUSDT
    /// </summary>
    private static string DenormalizeSymbol(string normalizedSymbol)
    {
        return normalizedSymbol.Replace("_", "");
    }

    public BinanceFuturesExchangeClient()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(BASE_URL) };
    }

    /// <summary>
    /// Get Futures symbols - returns ONLY whitelisted symbols
    /// GET /fapi/v1/exchangeInfo
    /// </summary>
    public async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/fapi/v1/exchangeInfo");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("symbols", out var symbolsArray))
            {
                Console.WriteLine($"[{ExchangeName}] Failed to parse exchangeInfo");
                return Enumerable.Empty<SymbolInfo>();
            }

            var symbolInfos = new List<SymbolInfo>();

            foreach (var symbol in symbolsArray.EnumerateArray())
            {
                if (!symbol.TryGetProperty("symbol", out var symbolName) ||
                    !symbol.TryGetProperty("status", out var status) ||
                    status.GetString() != "TRADING")
                    continue;

                var binanceSymbol = symbolName.GetString()!;
                var normalized = NormalizeSymbol(binanceSymbol);

                if (!WHITELISTED_SYMBOLS.Contains(normalized))
                    continue;

                // Parse filters for priceStep, quantityStep, minNotional
                decimal priceStep = 0.01m;
                decimal quantityStep = 0.001m;
                decimal minNotional = 0m;

                if (symbol.TryGetProperty("filters", out var filters))
                {
                    foreach (var filter in filters.EnumerateArray())
                    {
                        if (!filter.TryGetProperty("filterType", out var filterType))
                            continue;

                        var type = filterType.GetString();
                        if (type == "PRICE_FILTER" && filter.TryGetProperty("tickSize", out var tickSize))
                        {
                            priceStep = decimal.Parse(tickSize.GetString()!, CultureInfo.InvariantCulture);
                        }
                        else if (type == "LOT_SIZE" && filter.TryGetProperty("stepSize", out var stepSize))
                        {
                            quantityStep = decimal.Parse(stepSize.GetString()!, CultureInfo.InvariantCulture);
                        }
                        else if (type == "MIN_NOTIONAL" && filter.TryGetProperty("notional", out var notional))
                        {
                            minNotional = decimal.Parse(notional.GetString()!, CultureInfo.InvariantCulture);
                        }
                    }
                }

                symbolInfos.Add(new SymbolInfo
                {
                    Exchange = ExchangeName,
                    Name = normalized,
                    PriceStep = priceStep,
                    QuantityStep = quantityStep,
                    MinNotional = minNotional
                });
            }

            // FAIL-FAST: Ensure exactly 9 symbols loaded
            if (symbolInfos.Count != WHITELISTED_SYMBOLS.Count)
            {
                Console.WriteLine($"[{ExchangeName}] ⚠️  WARNING: Expected {WHITELISTED_SYMBOLS.Count} symbols, got {symbolInfos.Count}");
                var missing = WHITELISTED_SYMBOLS.Except(symbolInfos.Select(s => s.Name));
                if (missing.Any())
                {
                    Console.WriteLine($"[{ExchangeName}] Missing symbols: {string.Join(", ", missing)}");
                }
            }

            return symbolInfos;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{ExchangeName}] GetSymbolsAsync error: {ex.Message}");
            return Enumerable.Empty<SymbolInfo>();
        }
    }

    /// <summary>
    /// Get Futures tickers - returns ONLY whitelisted symbols
    /// GET /fapi/v1/ticker/24hr
    /// </summary>
    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/fapi/v1/ticker/24hr");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var tickers = new List<TickerData>();

            foreach (var ticker in doc.RootElement.EnumerateArray())
            {
                if (!ticker.TryGetProperty("symbol", out var symbolProp))
                    continue;

                var binanceSymbol = symbolProp.GetString()!;
                var normalized = NormalizeSymbol(binanceSymbol);

                if (!WHITELISTED_SYMBOLS.Contains(normalized))
                    continue;

                // Parse ticker data
                decimal priceChangePercent = 0m;
                decimal lastPrice = 0m;
                decimal quoteVolume = 0m;

                if (ticker.TryGetProperty("priceChangePercent", out var pcp))
                    priceChangePercent = Math.Max(-100, Math.Min(1000, decimal.Parse(pcp.GetString()!, CultureInfo.InvariantCulture)));

                if (ticker.TryGetProperty("lastPrice", out var lp))
                    lastPrice = decimal.Parse(lp.GetString()!, CultureInfo.InvariantCulture);

                if (ticker.TryGetProperty("quoteVolume", out var qv))
                    quoteVolume = decimal.Parse(qv.GetString()!, CultureInfo.InvariantCulture);

                tickers.Add(new TickerData
                {
                    Symbol = normalized,
                    QuoteVolume = quoteVolume,
                    Volume24h = quoteVolume,
                    PriceChangePercent24h = priceChangePercent,
                    LastPrice = lastPrice,
                    BestBid = 0,  // Not available in 24h ticker
                    BestAsk = 0   // Not available in 24h ticker
                });
            }

            return tickers;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{ExchangeName}] GetTickersAsync error: {ex.Message}");
            return Enumerable.Empty<TickerData>();
        }
    }

    /// <summary>
    /// Subscribe to trade updates using native WebSocket
    /// </summary>
    public async Task SubscribeToTradesAsync(IEnumerable<string> symbols, Func<TradeData, Task> onData)
    {
        var symbolsList = symbols.Where(s => WHITELISTED_SYMBOLS.Contains(s)).ToList();
        
        Console.WriteLine($"[{ExchangeName}] SubscribeToTradesAsync called with {symbolsList.Count} symbols");

        if (symbolsList.Count == 0)
        {
            Console.WriteLine($"[{ExchangeName}] No whitelisted symbols to subscribe");
            return;
        }

        _nativeWebSocket = new BinanceFuturesNativeWebSocketClient();
        await _nativeWebSocket.ConnectAsync();

        // Denormalize symbols for Binance API (BTC_USDT → BTCUSDT)
        var binanceSymbols = symbolsList.Select(DenormalizeSymbol).ToList();

        // Subscribe to each symbol individually
        foreach (var binanceSymbol in binanceSymbols)
        {
            await _nativeWebSocket.SubscribeToTradesAsync(binanceSymbol, async trade =>
            {
                // Create new TradeData with normalized symbol (Symbol is init-only)
                var normalizedTrade = new TradeData
                {
                    Exchange = trade.Exchange,
                    Symbol = NormalizeSymbol(trade.Symbol),
                    Price = trade.Price,
                    Quantity = trade.Quantity,
                    Side = trade.Side,
                    Timestamp = trade.Timestamp
                };
                await onData(normalizedTrade);
            });
        }

        Console.WriteLine($"[{ExchangeName}] ✅ Subscribed to {symbolsList.Count} symbols");
    }

    public Task StopAsync()
    {
        Console.WriteLine($"[{ExchangeName}] Stopping...");

        if (_nativeWebSocket != null)
        {
            _nativeWebSocket.Dispose();
            _nativeWebSocket = null;
        }

        _httpClient?.Dispose();

        Console.WriteLine($"[{ExchangeName}] Stopped");
        return Task.CompletedTask;
    }
}
