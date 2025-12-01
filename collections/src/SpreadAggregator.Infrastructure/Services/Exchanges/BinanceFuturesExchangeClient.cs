using Binance.Net.Clients;
using Binance.Net.Enums;
using CryptoExchange.Net.Sockets;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Binance Futures exchange client with hardcoded whitelist of 9 symbols.
/// REST API: Binance.Net for symbols/tickers
/// WebSocket: Binance.Net SocketClient for real-time trades
/// </summary>
public class BinanceFuturesExchangeClient : IExchangeClient
{
    private readonly BinanceRestClient _restClient;
    private BinanceSocketClient? _socketClient;

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
        _restClient = new BinanceRestClient();
    }

    /// <summary>
    /// Get Futures symbols - returns ONLY whitelisted symbols
    /// </summary>
    public async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        try
        {
            var exchangeInfo = await _restClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
            if (!exchangeInfo.Success || exchangeInfo.Data?.Symbols == null)
            {
                Console.WriteLine($"[{ExchangeName}] Failed to load exchange info");
                return Enumerable.Empty<SymbolInfo>();
            }

            var symbolInfos = exchangeInfo.Data.Symbols
                .Where(s => s.Status == SymbolStatus.Trading)
                .Select(s => new { Original = s, Normalized = NormalizeSymbol(s.Name) })
                .Where(x => WHITELISTED_SYMBOLS.Contains(x.Normalized))
                .Select(x => new SymbolInfo
                {
                    Exchange = ExchangeName,
                    Name = x.Normalized,  // Use normalized format (BTC_USDT)
                    PriceStep = x.Original.PriceFilter?.TickSize ?? 0.01m,
                    QuantityStep = x.Original.LotSizeFilter?.StepSize ?? 0.001m,
                    MinNotional = (decimal)(x.Original.MinNotionalFilter?.MinNotional ?? 0m)
                })
                .ToList();

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
    /// </summary>
    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        try
        {
            var tickersResult = await _restClient.UsdFuturesApi.ExchangeData.GetTickersAsync();
            if (!tickersResult.Success || tickersResult.Data == null)
            {
                Console.WriteLine($"[{ExchangeName}] Failed to load tickers");
                return Enumerable.Empty<TickerData>();
            }

            var tickers = tickersResult.Data
                .Select(t => new { Original = t, Normalized = NormalizeSymbol(t.Symbol) })
                .Where(x => WHITELISTED_SYMBOLS.Contains(x.Normalized))
                .Select(x =>
                {
                    // Binance returns price change as percentage (e.g., 2.5 for +2.5%)
                    decimal priceChangePercent = Math.Max(-100, Math.Min(1000, x.Original.PriceChangePercent));

                    return new TickerData
                    {
                        Symbol = x.Normalized,  // Use normalized format (BTC_USDT)
                        QuoteVolume = x.Original.QuoteVolume,
                        Volume24h = x.Original.QuoteVolume,
                        PriceChangePercent24h = priceChangePercent,
                        LastPrice = x.Original.LastPrice,
                        BestBid = 0,  // Not available in 24h ticker, would need separate orderbook call
                        BestAsk = 0   // Not available in 24h ticker, would need separate orderbook call
                    };
                })
                .ToList();

            return tickers;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{ExchangeName}] GetTickersAsync error: {ex.Message}");
            return Enumerable.Empty<TickerData>();
        }
    }

    /// <summary>
    /// Subscribe to trade updates using Binance WebSocket
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

        _socketClient = new BinanceSocketClient();

        // Denormalize symbols for Binance API (BTC_USDT → BTCUSDT)
        var binanceSymbols = symbolsList.Select(DenormalizeSymbol).ToList();

        // Subscribe to aggregated trades for all whitelisted symbols
        var subscriptionResult = await _socketClient.UsdFuturesApi.SubscribeToAggregatedTradeUpdatesAsync(
            binanceSymbols,
            async tradeUpdate =>
            {
                try
                {
                    var tradeData = new TradeData
                    {
                        Exchange = ExchangeName,
                        Symbol = NormalizeSymbol(tradeUpdate.Data.Symbol),  // Normalize: BTCUSDT → BTC_USDT
                        Price = tradeUpdate.Data.Price,
                        Quantity = tradeUpdate.Data.Quantity,
                        Timestamp = tradeUpdate.Data.TradeTime,
                        Side = tradeUpdate.Data.BuyerIsMaker ? "Sell" : "Buy"
                    };

                    await onData(tradeData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{ExchangeName}] Trade processing error for {tradeUpdate.Data.Symbol}: {ex.Message}");
                }
            });

        if (subscriptionResult.Success)
        {
            Console.WriteLine($"[{ExchangeName}] ✅ Subscribed to {symbolsList.Count} symbols");
        }
        else
        {
            Console.WriteLine($"[{ExchangeName}] ❌ Subscription failed: {subscriptionResult.Error?.Message}");
        }
    }

    public Task StopAsync()
    {
        Console.WriteLine($"[{ExchangeName}] Stopping...");

        if (_socketClient != null)
        {
            // Dispose automatically unsubscribes all subscriptions
            _socketClient.Dispose();
            _socketClient = null;
        }

        _restClient?.Dispose();

        Console.WriteLine($"[{ExchangeName}] Stopped");
        return Task.CompletedTask;
    }
}
