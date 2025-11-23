using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

public class TradeScreenerService : BackgroundService
{
    private readonly ChannelReader<MarketData> _channelReader;
    private readonly ILogger<TradeScreenerService> _logger;
    private readonly decimal _minTradeValueUsd;

    public TradeScreenerService(
        ChannelReader<MarketData> channelReader,
        IConfiguration configuration,
        ILogger<TradeScreenerService> logger)
    {
        _channelReader = channelReader;
        _logger = logger;
        _minTradeValueUsd = configuration.GetValue<decimal>("ScreenerSettings:MinTradeValueUsd", 10000);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"[TradeScreener] Starting screener. Min Value: ${_minTradeValueUsd:N0}");

        try
        {
            // Serilog handles log directory creation automatically

            await foreach (var data in _channelReader.ReadAllAsync(stoppingToken))
            {
                if (data is TradeData trade)
                {
                    var value = trade.Price * trade.Quantity;
                    if (value >= _minTradeValueUsd)
                    {
                        await LogWhaleTradeAsync(trade, value);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[TradeScreener] Screener stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TradeScreener] Error in screener loop.");
        }
    }

    private Task LogWhaleTradeAsync(TradeData trade, decimal value)
    {
        // Structured logging for Serilog (JSON support)
        _logger.LogInformation(
            "[WHALE] {Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Exchange} | {Symbol} | {Side} | Price: {Price} | Qty: {Quantity} | Value: ${Value:N2}",
            trade.Timestamp,
            trade.Exchange,
            trade.Symbol,
            trade.Side,
            trade.Price,
            trade.Quantity,
            value);

        return Task.CompletedTask;
    }
}
