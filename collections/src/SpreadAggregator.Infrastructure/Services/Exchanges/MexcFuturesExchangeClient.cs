using Mexc.Net.Clients;
using Mexc.Net.Interfaces.Clients.FuturesApi;
using Mexc.Net.Objects.Models.Futures;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// MEXC Futures exchange client implementation.
/// SPRINT 1: Implements futures market support using FuturesApi from Mexc.Net (>= 3.4.0)
/// </summary>
public class MexcFuturesExchangeClient : ExchangeClientBase<MexcRestClient, MexcSocketClient>
{
    public override string ExchangeName => "MexcFutures";

    // SPRINT 1: Start with ChunkSize = 1 for simplicity
    // Futures WebSocket API accepts only single symbol per subscription (unlike Spot which accepts multiple)
    // Each ManagedConnection will handle 1 symbol to avoid complexity
    protected override int ChunkSize => 1;

    protected override bool SupportsTradesStream => true;

    protected override MexcRestClient CreateRestClient() => new();
    protected override MexcSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(MexcSocketClient client)
    {
        return new MexcFuturesSocketApiAdapter(client.FuturesApi);
    }

    /// <summary>
    /// SPRINT 1.2: Get Futures symbols/contracts
    /// </summary>
    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
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
    /// SPRINT 1.3: Get Futures tickers
    /// </summary>
    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
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
                    // FUTURES ADVANTAGE: BestBid/BestAsk already in ticker! (no separate orderbook call needed)
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
    /// SPRINT 1.5: Get orderbook data for specific symbols (active/filtered symbols only)
    /// NOTE: For Futures, BestBid/BestAsk already in ticker, so this method is less critical
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
                else
                {
                    Console.WriteLine($"[MexcFutures] Orderbook failed for {symbol}: Success={orderbookResult.Success}");
                }
            }
            catch (Exception ex)
            {
                // Log but continue with other symbols
                Console.WriteLine($"[MexcFutures] Failed to get orderbook for {symbol}: {ex.Message}");
            }

            // Small delay to be respectful to API
            await Task.Delay(10);
        }

        Console.WriteLine($"[MexcFutures] Orderbook lookup completed: {successCount}/{symbols.Count()} symbols with bid/ask data");
        return orderbookLookup;
    }

    /// <summary>
    /// SPRINT 1.4: Adapter that wraps MEXC FuturesApi to implement IExchangeSocketApi.
    /// CRITICAL DIFFERENCE from Spot: Futures API accepts only SINGLE symbol per subscription!
    /// </summary>
    private class MexcFuturesSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IMexcSocketClientFuturesApi _futuresApi;

        public MexcFuturesSocketApiAdapter(IMexcSocketClientFuturesApi futuresApi)
        {
            _futuresApi = futuresApi;
        }

        public Task UnsubscribeAllAsync()
        {
            return _futuresApi.UnsubscribeAllAsync();
        }

        /// <summary>
        /// SPRINT 1.4: Subscribe to Futures trade updates
        /// CRITICAL: Futures API accepts only single symbol (not IEnumerable like Spot)
        /// </summary>
        public async Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Func<TradeData, Task> onData)
        {
            var subscriptions = new List<object>();

            // CRITICAL DIFFERENCE: Futures API only accepts ONE symbol per subscription
            // Must subscribe to each symbol individually
            foreach (var symbol in symbols)
            {
                try
                {
                    var result = await _futuresApi.SubscribeToTradeUpdatesAsync(
                        symbol,  // Single symbol only!
                        async data =>
                        {
                            // DEBUG: Log data arrival for first few symbols
                            if (symbol.Contains("1000") || symbol.Contains("LINK") || symbol.Contains("AVAX"))
                            {
                                Console.WriteLine($"[DEBUG] MexcFutures WS callback: symbol={symbol} data.Data={data?.Data?.GetType().Name ?? "NULL"}");
                            }

                            // Process trades - data.Data could be array or single object
                            if (data?.Data != null)
                            {
                                // Check if it's an array or single object
                                var trades = data.Data as IEnumerable<MexcFuturesTrade>;
                                if (trades != null)
                                {
                                    // It's an array
                                    foreach (var trade in trades)
                                    {
                                        await onData(new TradeData
                                        {
                                            Exchange = "MexcFutures",
                                            Symbol = symbol,
                                            Price = trade.Price,
                                            Quantity = trade.Quantity,
                                            Side = trade.Side == Mexc.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
                                            Timestamp = trade.Timestamp
                                        });
                                    }
                                }
                                else
                                {
                                    // It's a single object
                                    var trade = data.Data;
                                    await onData(new TradeData
                                    {
                                        Exchange = "MexcFutures",
                                        Symbol = symbol,
                                        Price = trade.Price,
                                        Quantity = trade.Quantity,
                                        Side = trade.Side == Mexc.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
                                        Timestamp = trade.Timestamp
                                    });
                                }
                            }
                        });

                    if (result.Success)
                    {
                        subscriptions.Add(result.Data);
                        // DEBUG: Log first 3 successful subscriptions
                        if (subscriptions.Count <= 3)
                        {
                            Console.WriteLine($"[MexcFutures] ✅ Successfully subscribed to {symbol}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[MexcFutures] ❌ Failed to subscribe to {symbol}: {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MexcFutures] ⚠️ Exception subscribing to {symbol}: {ex.Message}");
                }
            }

            // Return first subscription (or null if none succeeded)
            // ExchangeClientBase expects a single subscription object
            Console.WriteLine($"[MexcFutures] Subscription summary: {subscriptions.Count}/{symbols.Count()} symbols subscribed successfully");
            return subscriptions.FirstOrDefault() ?? new object();
        }
    }
}
