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
    private readonly string _logFilePath;

    public TradeScreenerService(
        ChannelReader<MarketData> channelReader,
        IConfiguration configuration,
        ILogger<TradeScreenerService> logger)
    {
        _channelReader = channelReader;
        _logger = logger;
        _minTradeValueUsd = configuration.GetValue<decimal>("ScreenerSettings:MinTradeValueUsd", 10000);
        _logFilePath = configuration.GetValue<string>("ScreenerSettings:LogFilePath") 
                       ?? Path.Combine("..", "..", "logs", "screener_trades.log");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"[TradeScreener] Starting screener. Min Value: ${_minTradeValueUsd:N0}");

        try
        {
            // Ensure log directory exists
            var logDir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

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

    private async Task LogWhaleTradeAsync(TradeData trade, decimal value)
    {
        var message = $"{trade.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {trade.Exchange} | {trade.Symbol} | {trade.Side} | Price: {trade.Price} | Qty: {trade.Quantity} | Value: ${value:N2}";
        
        // Log to console/logger
        _logger.LogInformation($"[WHALE] {message}");

        // Log to file
        try
        {
            await File.AppendAllTextAsync(_logFilePath, message + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[TradeScreener] Failed to write to log file: {ex.Message}");
        }
    }
}
