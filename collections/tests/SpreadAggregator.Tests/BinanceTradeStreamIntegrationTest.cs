using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using SpreadAggregator.Infrastructure.Services.Exchanges;
using SpreadAggregator.Domain.Entities;
using System.Collections.Concurrent;

namespace SpreadAggregator.Tests;

/// <summary>
/// REAL integration test - connects to actual Binance WebSocket
/// and listens to @trade stream for 60 seconds
/// </summary>
public class BinanceTradeStreamIntegrationTest
{
    private readonly ITestOutputHelper _output;

    public BinanceTradeStreamIntegrationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 90000)] // 90 second max timeout
    public async Task RealBinanceConnection_Should_Receive_Trade_Messages_For_60_Seconds()
    {
        // Arrange
        var client = new BinanceFuturesNativeWebSocketClient();
        var receivedTrades = new ConcurrentBag<TradeData>();
        var startTime = DateTime.UtcNow;
        var testDuration = TimeSpan.FromSeconds(60);
        
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting integration test...");
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Will listen for {testDuration.TotalSeconds} seconds");

        try
        {
            // Act: Connect to real Binance WebSocket
            await client.ConnectAsync();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Connected to Binance WebSocket");

            // Subscribe to BTC trades on @trade stream
            await client.SubscribeToTradesAsync("BTCUSDT", async (trade) =>
            {
                receivedTrades.Add(trade);
                
                if (receivedTrades.Count % 10 == 0)
                {
                    _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received {receivedTrades.Count} trades | Last: {trade.Symbol} @ {trade.Price} | Side: {trade.Side}");
                }
                
                await Task.CompletedTask;
            });
            
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Subscribed to BTCUSDT @trade stream");
            
            // Wait for 60 seconds
            var elapsed = TimeSpan.Zero;
            while (elapsed < testDuration)
            {
                await Task.Delay(5000); // Check every 5 seconds
                elapsed = DateTime.UtcNow - startTime;
                
                var remaining = testDuration - elapsed;
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Running... {receivedTrades.Count} trades received | {remaining.TotalSeconds:F0}s remaining");
            }

            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⏱️ Test duration completed");
            
            // Assert: We should have received trades
            Assert.True(receivedTrades.Count > 0, "Should receive at least 1 trade message");
            
            // Verify all received trades have correct Exchange
            foreach (var trade in receivedTrades)
            {
                Assert.Equal("Binance", trade.Exchange);
                Assert.NotNull(trade.Symbol);
                Assert.True(trade.Price > 0, "Price should be positive");
            }
            
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ TEST PASSED");
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Total trades received: {receivedTrades.Count}");
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] Average: {receivedTrades.Count / testDuration.TotalSeconds:F2} trades/sec");
        }
        finally
        {
            // Cleanup
            await client.DisconnectAsync();
            client.Dispose();
            _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✅ Disconnected and cleaned up");
        }
    }
}
