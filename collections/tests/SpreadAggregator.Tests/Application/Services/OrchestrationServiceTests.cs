using Moq;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using System.Text.Json;
using System.Threading.Channels;
using SpreadAggregator.Domain.Services;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace SpreadAggregator.Tests.Application.Services;

public class OrchestrationServiceTests
{
    [Fact]
    public async Task ProcessExchange_Should_SubscribeToTrades_ForValidSymbols()
    {
        // Arrange
        var mockWebSocketServer = new Mock<IWebSocketServer>();
        var volumeFilter = new VolumeFilter();
        var mockConfiguration = new Mock<IConfiguration>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var tradeScreenerChannel = Channel.CreateUnbounded<MarketData>();

        var exchangeName = "TestExchange";

        var tickers = new List<TickerData>
        {
            new TickerData { Symbol = "BTCUSDT", QuoteVolume = 2000000 }
        };
        
        var symbols = new List<SymbolInfo>
        {
            new SymbolInfo { Name = "BTCUSDT" }
        };

        mockExchangeClient.Setup(c => c.ExchangeName).Returns(exchangeName);
        mockExchangeClient.Setup(c => c.GetTickersAsync()).ReturnsAsync(tickers);
        mockExchangeClient.Setup(c => c.GetSymbolsAsync()).ReturnsAsync(symbols);
        mockExchangeClient.Setup(c => c.SubscribeToTradesAsync(
            It.Is<IEnumerable<string>>(s => s.Contains("BTCUSDT")),
            It.IsAny<Func<TradeData, Task>>()))
            .Returns(Task.CompletedTask);

        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(a => a.Key).Returns(exchangeName);
        var configSections = new List<IConfigurationSection> { configSection.Object };
        var mockConfig = new Mock<IConfigurationSection>();
        mockConfig.Setup(a => a.GetChildren()).Returns(configSections);
        mockConfiguration.Setup(c => c.GetSection("ExchangeSettings:Exchanges")).Returns(mockConfig.Object);
        mockConfiguration.Setup(c => c.GetSection(It.Is<string>(s => s.EndsWith(":VolumeFilter")))).Returns(new Mock<IConfigurationSection>().Object);
        mockConfiguration.Setup(c => c.GetValue<bool>("StreamSettings:EnableTrades", true)).Returns(true);

        var orchestrationService = new OrchestrationService(
            mockWebSocketServer.Object,
            mockConfiguration.Object,
            volumeFilter,
            new[] { mockExchangeClient.Object },
            tradeScreenerChannel
        );

        // Act
        await orchestrationService.StartAsync();

        // Assert
        mockExchangeClient.Verify(c => c.SubscribeToTradesAsync(
            It.Is<IEnumerable<string>>(s => s.Contains("BTCUSDT")),
            It.IsAny<Func<TradeData, Task>>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExchange_Should_FilterOutLowVolumeSymbols()
    {
        // Arrange
        var mockWebSocketServer = new Mock<IWebSocketServer>();
        var volumeFilter = new VolumeFilter();
        var mockConfiguration = new Mock<IConfiguration>();
        var mockExchangeClient = new Mock<IExchangeClient>();
        var tradeScreenerChannel = Channel.CreateUnbounded<MarketData>();

        var exchangeName = "TestExchange";

        // Low volume symbol
        var tickers = new List<TickerData>
        {
            new TickerData { Symbol = "TRASHCOIN", QuoteVolume = 100 } 
        };
        
        var symbols = new List<SymbolInfo>
        {
            new SymbolInfo { Name = "TRASHCOIN" }
        };

        mockExchangeClient.Setup(c => c.ExchangeName).Returns(exchangeName);
        mockExchangeClient.Setup(c => c.GetTickersAsync()).ReturnsAsync(tickers);
        mockExchangeClient.Setup(c => c.GetSymbolsAsync()).ReturnsAsync(symbols);

        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(a => a.Key).Returns(exchangeName);
        var configSections = new List<IConfigurationSection> { configSection.Object };
        var mockConfig = new Mock<IConfigurationSection>();
        mockConfig.Setup(a => a.GetChildren()).Returns(configSections);
        mockConfiguration.Setup(c => c.GetSection("ExchangeSettings:Exchanges")).Returns(mockConfig.Object);
        
        // Setup MinUsdVolume = 1000
        var volumeConfig = new Mock<IConfigurationSection>();
        volumeConfig.Setup(c => c.GetValue<decimal?>("MinUsdVolume", null)).Returns(1000m);
        mockConfiguration.Setup(c => c.GetSection($"ExchangeSettings:Exchanges:{exchangeName}:VolumeFilter")).Returns(volumeConfig.Object);

        var orchestrationService = new OrchestrationService(
            mockWebSocketServer.Object,
            mockConfiguration.Object,
            volumeFilter,
            new[] { mockExchangeClient.Object },
            tradeScreenerChannel
        );

        // Act
        await orchestrationService.StartAsync();

        // Assert
        mockExchangeClient.Verify(c => c.SubscribeToTradesAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<Func<TradeData, Task>>()), Times.Never);
    }
}