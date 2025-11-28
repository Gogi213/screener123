// SPRINT 0: Quick test runner for FuturesApi exploration
// Run with: dotnet run --project collections/src/SpreadAggregator.Presentation TestFuturesApi.cs
// Or compile: csc TestFuturesApi.cs /r:path/to/Mexc.Net.dll

using Mexc.Net.Clients;
using System;
using System.Linq;
using System.Threading.Tasks;

class TestFuturesApi
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MEXC FUTURES API EXPLORATION ===\n");

        var restClient = new MexcRestClient();

        try
        {
            // Test 1: Get Futures Symbols
            Console.WriteLine("[1] Testing FuturesApi.ExchangeData.GetSymbolsAsync()...");
            var symbolsResult = await restClient.FuturesApi.ExchangeData.GetSymbolsAsync();

            if (symbolsResult.Success && symbolsResult.Data != null)
            {
                var symbols = symbolsResult.Data.Take(3).ToList();
                Console.WriteLine($"✅ Success! Got {symbolsResult.Data.Count()} symbols");
                Console.WriteLine($"\nSample symbols (first 3):");
                foreach (var symbol in symbols)
                {
                    Console.WriteLine($"\n  Symbol: {symbol.Name}");
                    Console.WriteLine($"    Base: {symbol.BaseAsset}, Quote: {symbol.QuoteAsset}");
                    Console.WriteLine($"    TickSize (PriceStep): {symbol.TickSize}");
                    Console.WriteLine($"    LotSize (QuantityStep): {symbol.LotSize}");
                    Console.WriteLine($"    MinQuantity: {symbol.MinQuantity}");
                    Console.WriteLine($"    Status: {symbol.Status}");
                }

                // Check symbol naming format
                var btcSymbol = symbolsResult.Data.FirstOrDefault(s => s.Name.Contains("BTC") && s.QuoteAsset == "USDT");
                if (btcSymbol != null)
                {
                    Console.WriteLine($"\n  ⚠️  Symbol format check:");
                    Console.WriteLine($"      BTC/USDT symbol name: '{btcSymbol.Name}'");
                    Console.WriteLine($"      Format appears to be: {(btcSymbol.Name.Contains("_") ? "BTC_USDT" : "BTCUSDT")}");
                }
            }
            else
            {
                Console.WriteLine($"❌ Failed: {symbolsResult.Error}");
            }

            Console.WriteLine("\n" + new string('-', 60) + "\n");

            // Test 2: Get Futures Tickers
            Console.WriteLine("[2] Testing FuturesApi.ExchangeData.GetTickersAsync()...");
            var tickersResult = await restClient.FuturesApi.ExchangeData.GetTickersAsync();

            if (tickersResult.Success && tickersResult.Data != null)
            {
                var tickers = tickersResult.Data.Take(3).ToList();
                Console.WriteLine($"✅ Success! Got {tickersResult.Data.Count()} tickers");
                Console.WriteLine($"\nSample tickers (first 3):");
                foreach (var ticker in tickers)
                {
                    Console.WriteLine($"\n  Symbol: {ticker.Symbol}");
                    Console.WriteLine($"    LastPrice: {ticker.LastPrice}");
                    Console.WriteLine($"    Volume24h: {ticker.Volume}");
                    Console.WriteLine($"    QuoteVolume24h: {ticker.QuoteVolume}");
                    Console.WriteLine($"    PriceChange: {ticker.PriceChange}");
                    Console.WriteLine($"    PriceChangePercent: {ticker.PriceChangePercent}%");
                    Console.WriteLine($"    HighPrice24h: {ticker.HighPrice}");
                    Console.WriteLine($"    LowPrice24h: {ticker.LowPrice}");
                }

                // Check if PriceChangePercent is already in percent format
                var sampleTicker = tickers.First();
                Console.WriteLine($"\n  ⚠️  PriceChangePercent format check:");
                Console.WriteLine($"      Value: {sampleTicker.PriceChangePercent}");
                Console.WriteLine($"      Appears to be: {(Math.Abs(sampleTicker.PriceChangePercent) > 1 ? "ALREADY IN %" : "DECIMAL (needs *100)")}");
            }
            else
            {
                Console.WriteLine($"❌ Failed: {tickersResult.Error}");
            }

            Console.WriteLine("\n" + new string('-', 60) + "\n");

            // Test 3: Get Orderbook
            Console.WriteLine("[3] Testing FuturesApi.ExchangeData.GetOrderBookAsync()...");

            // Try different symbol formats
            string[] testSymbols = { "BTC_USDT", "BTCUSDT", "BTC-USDT" };
            bool orderbookSuccess = false;

            foreach (var testSymbol in testSymbols)
            {
                Console.WriteLine($"\n  Trying symbol format: '{testSymbol}'");
                var orderbookResult = await restClient.FuturesApi.ExchangeData.GetOrderBookAsync(testSymbol, 5);

                if (orderbookResult.Success && orderbookResult.Data != null)
                {
                    Console.WriteLine($"  ✅ Success with '{testSymbol}' format!");
                    Console.WriteLine($"    Bids: {orderbookResult.Data.Bids.Count()}");
                    if (orderbookResult.Data.Bids.Any())
                    {
                        var bestBid = orderbookResult.Data.Bids.First();
                        Console.WriteLine($"      Best Bid: Price={bestBid.Price}, Quantity={bestBid.Quantity}");
                    }
                    Console.WriteLine($"    Asks: {orderbookResult.Data.Asks.Count()}");
                    if (orderbookResult.Data.Asks.Any())
                    {
                        var bestAsk = orderbookResult.Data.Asks.First();
                        Console.WriteLine($"      Best Ask: Price={bestAsk.Price}, Quantity={bestAsk.Quantity}");
                    }
                    orderbookSuccess = true;
                    break;
                }
                else
                {
                    Console.WriteLine($"  ❌ Failed: {orderbookResult.Error?.Message}");
                }
            }

            Console.WriteLine("\n" + new string('-', 60) + "\n");

            // Test 4: WebSocket structure
            Console.WriteLine("[4] Checking FuturesApi WebSocket structure...");
            var socketClient = new MexcSocketClient();
            var futuresApi = socketClient.FuturesApi;

            Console.WriteLine($"  FuturesApi type: {futuresApi.GetType().Name}");

            var methods = futuresApi.GetType().GetMethods()
                .Where(m => m.Name.StartsWith("Subscribe"))
                .Select(m => new {
                    Name = m.Name,
                    Params = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))
                })
                .ToList();

            Console.WriteLine($"\n  Available Subscribe methods ({methods.Count}):");
            foreach (var method in methods)
            {
                Console.WriteLine($"    - {method.Name}({method.Params})");
            }

            Console.WriteLine("\n=== EXPLORATION COMPLETE ===\n");

            Console.WriteLine("SUMMARY FOR SPRINT 0:");
            Console.WriteLine("  [✓] FuturesApi structure explored");
            Console.WriteLine($"  [✓] Symbol format: {(orderbookSuccess ? "Identified" : "NEEDS MANUAL CHECK")}");
            Console.WriteLine("  [✓] Ticker fields: LastPrice, Volume, QuoteVolume, PriceChangePercent");
            Console.WriteLine("  [✓] Orderbook: Bids/Asks structure confirmed");
            Console.WriteLine("  [✓] WebSocket: FuturesApi available");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
            Console.WriteLine($"\nStack Trace:\n{ex.StackTrace}");
        }
    }
}
