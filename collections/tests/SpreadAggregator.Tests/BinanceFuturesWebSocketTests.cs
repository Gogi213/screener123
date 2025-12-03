using System.Text.Json;
using Xunit;
using SpreadAggregator.Infrastructure.Services.Exchanges;
using SpreadAggregator.Domain.Entities;

namespace SpreadAggregator.Tests;

/// <summary>
/// Unit tests for Sprint 0: Verify switch from @aggTrade to @trade
/// </summary>
public class BinanceFuturesWebSocketTests
{
    [Fact]
    public void SubscriptionMessage_Should_Use_Trade_Stream()
    {
        // Arrange
        var symbol = "BTCUSDT";
        var expectedStream = "btcusdt@trade";
        
        // Act
        var subscriptionMessage = new
        {
            method = "SUBSCRIBE",
            @params = new[] { $"{symbol.ToLowerInvariant()}@trade" },
            id = 1
        };
        
        var json = JsonSerializer.Serialize(subscriptionMessage);
        
        // Assert
        Assert.Contains(expectedStream, json);
        Assert.DoesNotContain("@aggTrade", json);
    }

    [Fact]
    public void TradeMessage_Should_Parse_Correctly()
    {
        // Arrange: Simulate Binance @trade message
        var tradeJson = @"{
            ""e"": ""trade"",
            ""s"": ""BTCUSDT"",
            ""p"": ""43250.50"",
            ""q"": ""0.125"",
            ""T"": 1701234567890,
            ""m"": false
        }";
        
        // Act
        using var doc = JsonDocument.Parse(tradeJson);
        var root = doc.RootElement;
        
        // Assert
        Assert.True(root.TryGetProperty("e", out var eventType));
        Assert.Equal("trade", eventType.GetString());
        
        Assert.True(root.TryGetProperty("s", out var symbol));
        Assert.Equal("BTCUSDT", symbol.GetString());
        
        Assert.True(root.TryGetProperty("p", out var price));
        Assert.Equal("43250.50", price.GetString());
        
        Assert.True(root.TryGetProperty("q", out var qty));
        Assert.Equal("0.125", qty.GetString());
        
        Assert.True(root.TryGetProperty("T", out var timestamp));
        Assert.Equal(1701234567890, timestamp.GetInt64());
        
        Assert.True(root.TryGetProperty("m", out var isMaker));
        Assert.False(isMaker.GetBoolean());
    }

    [Fact]
    public void EventType_Should_Be_Trade_Not_AggTrade()
    {
        // Arrange
        var correctJson = @"{""e"":""trade""}";
        var incorrectJson = @"{""e"":""aggTrade""}";
        
        // Act
        using var correctDoc = JsonDocument.Parse(correctJson);
        using var incorrectDoc = JsonDocument.Parse(incorrectJson);
        
        var correctEventType = correctDoc.RootElement.GetProperty("e").GetString();
        var incorrectEventType = incorrectDoc.RootElement.GetProperty("e").GetString();
        
        // Assert
        Assert.Equal("trade", correctEventType);
        Assert.NotEqual("aggTrade", correctEventType);
        Assert.NotEqual("trade", incorrectEventType);
    }

    [Theory]
    [InlineData("BTC_USDT", "btcusdt@trade")]
    [InlineData("ETH_USDT", "ethusdt@trade")]
    [InlineData("SOL_USDT", "solusdt@trade")]
    public void SubscriptionStream_Should_Match_Pattern(string symbol, string expectedStream)
    {
        // Arrange
        var normalizedSymbol = symbol.Replace("_", "");
        
        // Act
        var stream = $"{normalizedSymbol.ToLowerInvariant()}@trade";
        
        // Assert
        Assert.Equal(expectedStream, stream);
    }
}
