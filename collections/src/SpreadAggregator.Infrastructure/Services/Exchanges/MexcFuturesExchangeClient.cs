using Mexc.Net.Clients;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// MEXC Futures exchange client implementation using native WebSocket.
/// REST API: Mexc.Net for symbols/tickers
/// WebSocket: Native client for push.deal (real-time trades)
/// </summary>
public class MexcFuturesExchangeClient : IExchangeClient
{
    private readonly MexcRestClient _restClient;
    private MexcFuturesNativeWebSocketClient? _nativeWebSocket;

    public string ExchangeName => "MexcFutures";

    public MexcFuturesExchangeClient()
    {
        _restClient = new MexcRestClient();
    }

    /// <summary>
    /// Get Futures symbols/contracts via REST API
    /// </summary>
    public async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.FuturesApi.ExchangeData.GetSymbolsAsync();
        if (!symbolsData.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Symbol,  // Contract symbol name (e.g., "BTC_USDT")
            PriceStep = s.PriceUnit,  // Price increment
            QuantityStep = s.VolumeUnit,  // Volume/quantity step
            MinNotional = s.MinQuantity  // Minimum order quantity
        });
    }

    /// <summary>
    /// Get Futures tickers via REST API
    /// </summary>
    public async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        try
        {
            var tickersResult = await _restClient.FuturesApi.ExchangeData.GetTickersAsync();
            if (!tickersResult.Success || tickersResult.Data == null)
            {
                return Enumerable.Empty<TickerData>();
            }

            var tickersList = tickersResult.Data.Select(t =>
            {
                // MEXC Futures API returns ChangePercentage already in percent format
                // Validate and clamp to reasonable range [-100%, +1000%]
                decimal priceChangePercent = Math.Max(-100, Math.Min(1000, t.ChangePercentage));

                var ticker = new TickerData
                {
                    Symbol = t.Symbol,
                    QuoteVolume = t.QuoteVolume24h,
                    Volume24h = t.QuoteVolume24h,  // 24h quote volume
                    PriceChangePercent24h = priceChangePercent,
                    LastPrice = t.LastPrice,
                    // FUTURES ADVANTAGE: BestBid/BestAsk already in ticker!
                    BestBid = t.BestBidPrice,
                    BestAsk = t.BestAskPrice
                };
                return ticker;
            }).ToList();

            // DEBUG: Log first 3 tickers to verify data format
            if (tickersList.Any())
            {
                var sample = tickersList.Take(3);
                foreach (var t in sample)
                {
                    Console.WriteLine($"[MexcFutures] DEBUG Ticker: {t.Symbol} | Vol24h={t.Volume24h:F2} | Bid={t.BestBid:F8} | Ask={t.BestAsk:F8}");
                }
            }

            return tickersList;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MexcFutures] Ticker refresh error: {ex.Message}");
            return Enumerable.Empty<TickerData>();
        }
    }

    /// <summary>
    /// Subscribe to trade updates using native WebSocket client (push.deal channel)
    /// </summary>
    public async Task SubscribeToTradesAsync(IEnumerable<string> symbols, Func<TradeData, Task> onData)
    {
        Console.WriteLine($"[MexcFuturesNative] SubscribeToTradesAsync called with {symbols.Count()} symbols");

        // Create and connect native WebSocket client
        _nativeWebSocket = new MexcFuturesNativeWebSocketClient();
        await _nativeWebSocket.ConnectAsync();

        Console.WriteLine($"[MexcFuturesNative] Subscribing to {symbols.Count()} symbols...");

        // Subscribe to each symbol
        var subscriptionTasks = symbols.Select(async symbol =>
        {
            try
            {
                await _nativeWebSocket.SubscribeToTradesAsync(symbol, async (tradeData) =>
                {
                    // Call callback with trade data
                    await onData(tradeData);
                });

                Console.WriteLine($"[MexcFuturesNative] ✅ Subscribed to {symbol}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MexcFuturesNative] ❌ Failed to subscribe to {symbol}: {ex.Message}");
            }
        });

        await Task.WhenAll(subscriptionTasks);

        Console.WriteLine($"[MexcFuturesNative] All subscriptions complete!");
    }

    /// <summary>
    /// Get orderbook data for specific symbols (BestBid/BestAsk already in ticker, but this is for accuracy)
    /// </summary>
    public async Task<Dictionary<string, (decimal bid, decimal ask)>> GetOrderbookForSymbolsAsync(IEnumerable<string> symbols)
    {
        var orderbookLookup = new Dictionary<string, (decimal bid, decimal ask)>();
        int successCount = 0;

        // Get orderbooks sequentially to avoid overwhelming the API
        foreach (var symbol in symbols)
        {
            try
            {
                var orderbookResult = await _restClient.FuturesApi.ExchangeData.GetOrderBookAsync(symbol, 5);
                if (orderbookResult.Success &&
                    orderbookResult.Data?.Bids?.Any() == true &&
                    orderbookResult.Data?.Asks?.Any() == true)
                {
                    var bestBid = orderbookResult.Data.Bids.First().Price;
                    var bestAsk = orderbookResult.Data.Asks.First().Price;
                    orderbookLookup[symbol] = (bestBid, bestAsk);
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MexcFutures] Failed to get orderbook for {symbol}: {ex.Message}");
            }

            await Task.Delay(10);
        }

        Console.WriteLine($"[MexcFutures] Orderbook lookup completed: {successCount}/{symbols.Count()} symbols with bid/ask data");
        return orderbookLookup;
    }

    public async Task StopAsync()
    {
        Console.WriteLine($"[MexcFuturesNative] Stopping...");

        if (_nativeWebSocket != null)
        {
            await _nativeWebSocket.DisconnectAsync();
            _nativeWebSocket.Dispose();
            _nativeWebSocket = null;
        }

        Console.WriteLine($"[MexcFuturesNative] Stopped");
    }
}
