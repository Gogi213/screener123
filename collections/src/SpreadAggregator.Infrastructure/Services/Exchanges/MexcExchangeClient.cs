using Mexc.Net.Clients;
using Mexc.Net.Interfaces.Clients.SpotApi;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Infrastructure.Services.Exchanges.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// MEXC exchange client implementation.
/// Reduced from 152 lines to ~115 lines using ExchangeClientBase.
/// </summary>
public class MexcExchangeClient : ExchangeClientBase<MexcRestClient, MexcSocketClient>
{
    public override string ExchangeName => "MEXC";
    // MEXC has a limit of 30 subscriptions per connection. We use 20% of that.
    // MEXC has a limit on the message size for subscriptions.
    // A chunkSize of 30 was too large and exceeded the 1024 byte limit.
    // Reducing to 6 to keep message size down.
    protected override int ChunkSize => 6;
    protected override bool SupportsTradesStream => true;

    protected override MexcRestClient CreateRestClient() => new();
    protected override MexcSocketClient CreateSocketClient() => new();

    protected override IExchangeSocketApi CreateSocketApi(MexcSocketClient client)
    {
        return new MexcSocketApiAdapter(client.SpotApi);
    }

    public override async Task<IEnumerable<SymbolInfo>> GetSymbolsAsync()
    {
        var symbolsData = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
        if (!symbolsData.Success)
        {
            return Enumerable.Empty<SymbolInfo>();
        }

        return symbolsData.Data.Symbols.Select(s => new SymbolInfo
        {
            Exchange = ExchangeName,
            Name = s.Name,
            PriceStep = (decimal)Math.Pow(10, -s.QuoteAssetPrecision),
            QuantityStep = (decimal)Math.Pow(10, -s.BaseAssetPrecision),
            MinNotional = s.QuoteQuantityPrecision
        });
    }

    public override async Task<IEnumerable<TickerData>> GetTickersAsync()
    {
        try
        {
            // Get tickers first
            var tickersResult = await _restClient.SpotApi.ExchangeData.GetTickersAsync();
            if (!tickersResult.Success || tickersResult.Data == null)
            {
                return Enumerable.Empty<TickerData>();
            }

            var tickers = tickersResult.Data;

            // Orderbook data will be fetched separately for active symbols only

            return tickers.Select(t =>
            {
                // MEXC API returns PriceChange already in percent format (e.g., 5.25 = 5.25%)
                // Do NOT multiply by 100, it's already a percentage
                decimal priceChangePercent = t.PriceChange;

                // Validate and clamp to reasonable range
                // New coins or data anomalies can produce extreme values
                // Clamp to [-100%, +1000%] range to filter out obvious errors from MEXC API
                priceChangePercent = Math.Max(-100, Math.Min(1000, priceChangePercent));

                return new TickerData
                {
                    Symbol = t.Symbol,
                    QuoteVolume = t.QuoteVolume ?? 0,
                    // SPRINT-10: Add 24h metrics from MEXC ticker
                    Volume24h = t.QuoteVolume ?? 0,  // MEXC QuoteVolume is already 24h
                    PriceChangePercent24h = priceChangePercent,
                    LastPrice = t.LastPrice,
                    // SPRINT-12: BestBid/BestAsk will be populated separately for active symbols
                    BestBid = 0,
                    BestAsk = 0
                };
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MEXC] Ticker refresh error: {ex.Message}");
            return Enumerable.Empty<TickerData>();
        }
    }

    /// <summary>
    /// SPRINT-12: Get orderbook data for specific symbols (active/filtered symbols only)
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
                var orderbookResult = await _restClient.SpotApi.ExchangeData.GetOrderBookAsync(symbol, 5);
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
                    Console.WriteLine($"[MEXC] Orderbook failed for {symbol}: Success={orderbookResult.Success}");
                }
            }
            catch (Exception ex)
            {
                // Log but continue with other symbols
                Console.WriteLine($"[MEXC] Failed to get orderbook for {symbol}: {ex.Message}");
            }

            // Small delay to be respectful to API
            await Task.Delay(10);
        }

        Console.WriteLine($"[MEXC] Orderbook lookup completed: {successCount}/{symbols.Count()} symbols with bid/ask data");
        return orderbookLookup;
    }

    /// <summary>
    /// Adapter that wraps MEXC SpotApi to implement IExchangeSocketApi.
    /// </summary>
    private class MexcSocketApiAdapter : IExchangeSocketApi
    {
        private readonly IMexcSocketClientSpotApi _spotApi;

        public MexcSocketApiAdapter(IMexcSocketClientSpotApi spotApi)
        {
            _spotApi = spotApi;
        }

        public Task UnsubscribeAllAsync()
        {
            return _spotApi.UnsubscribeAllAsync();
        }



        public async Task<object> SubscribeToTradeUpdatesAsync(
            IEnumerable<string> symbols,
            Func<TradeData, Task> onData)
        {
            // Mexc requires interval parameter (100ms = 10 updates per second)
            var result = await _spotApi.SubscribeToTradeUpdatesAsync(
                symbols,
                100, // interval in milliseconds
                async data =>
                {
                    if (data.Data != null && data.Symbol != null)
                    {
                        // Mexc returns array of trades
                        foreach (var trade in data.Data)
                        {
                            await onData(new TradeData
                            {
                                Exchange = "MEXC",
                                Symbol = data.Symbol,
                                Price = trade.Price,
                                Quantity = trade.Quantity,
                                Side = trade.Side == Mexc.Net.Enums.OrderSide.Buy ? "Buy" : "Sell",
                                Timestamp = trade.Timestamp
                            });
                        }
                    }
                });

            return result;
        }
    }
}
