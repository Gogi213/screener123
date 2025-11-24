using Grpc.Core;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Grpc;

namespace SpreadAggregator.Presentation.Services;

/// <summary>
/// gRPC service for streaming real-time trade data
/// Implements server-side filtering and sorting for optimal performance
/// </summary>
public class TradeStreamerService : TradeStreamer.TradeStreamerBase
{
    private readonly TradeAggregatorService _tradeAggregator;
    private readonly ILogger<TradeStreamerService> _logger;

    public TradeStreamerService(
        TradeAggregatorService tradeAggregator,
        ILogger<TradeStreamerService> logger)
    {
        _tradeAggregator = tradeAggregator;
        _logger = logger;
    }

    /// <summary>
    /// Get all symbols metadata (sorted by activity)
    /// </summary>
    public override Task<SymbolsResponse> GetSymbols(EmptyRequest request, ServerCallContext context)
    {
        var metadata = _tradeAggregator.GetAllSymbolsMetadata().ToList();

        var response = new SymbolsResponse
        {
            TotalSymbols = metadata.Count,
            TotalPages = (int)Math.Ceiling(metadata.Count / 100.0)
        };

        // Convert from Application.Services.SymbolMetadata to Grpc.SymbolMetadata (Protobuf)
        foreach (var meta in metadata)
        {
            response.Symbols.Add(new Grpc.SymbolMetadata
            {
                Symbol = meta.Symbol,
                LastPrice = (double)meta.LastPrice,
                LastUpdate = meta.LastUpdate.Ticks / TimeSpan.TicksPerMillisecond,
                TradesPerMin = meta.TradesPerMin,
                Rank = metadata.IndexOf(meta) + 1  // 1-indexed rank
            });
        }

        _logger.LogInformation("[gRPC] GetSymbols: returning {Count} symbols", metadata.Count);
        return Task.FromResult(response);
    }

    /// <summary>
    /// Stream real-time trades for a specific page
    /// SERVER-SIDE FILTERING: Only sends updates for symbols on requested page
    /// </summary>
    public override async Task StreamTrades(
        StreamRequest request,
        IServerStreamWriter<TradeUpdate> responseStream,
        ServerCallContext context)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 100;

        _logger.LogInformation("[gRPC] Client subscribed to page {Page} (pageSize: {PageSize})", page, pageSize);

        // Get symbols for this page (server-side filtering!)
        var allMetadata = _tradeAggregator.GetAllSymbolsMetadata().ToList();
        var symbolsOnPage = allMetadata
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => $"MEXC_{m.Symbol}")
            .ToHashSet();

        // Send initial data for these symbols
        var initialData = _tradeAggregator.GetTradesForSymbols(symbolsOnPage);
        foreach (var (symbolKey, trades) in initialData)
        {
            var update = new TradeUpdate { Symbol = symbolKey };
            
            foreach (var trade in trades)
            {
                update.Trades.Add(new Trade
                {
                    Price = (double)trade.Price,
                    Quantity = (double)trade.Quantity,
                    Side = trade.Side,
                    Timestamp = trade.Timestamp.Ticks / TimeSpan.TicksPerMillisecond
                });
            }

            await responseStream.WriteAsync(update);
        }

        _logger.LogInformation("[gRPC] Sent initial data for {Count} symbols", initialData.Count);

        // Real-time streaming: poll for new trades and send updates
        var lastCheck = DateTime.UtcNow;
        
        try
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, context.CancellationToken); // Match batch interval
                
                // Get fresh symbol list (it may change due to sorting)
                var currentMetadata = _tradeAggregator.GetAllSymbolsMetadata().ToList();
                var currentSymbolsOnPage = currentMetadata
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => $"MEXC_{m.Symbol}")
                    .ToHashSet();
                
                // Get latest trades for symbols on this page
                var latestTrades = _tradeAggregator.GetTradesForSymbols(currentSymbolsOnPage);
                
                foreach (var (symbolKey, trades) in latestTrades)
                {
                    // Filter trades that arrived since last check
                    var newTrades = trades.Where(t => t.Timestamp > lastCheck).ToList();
                    
                    if (newTrades.Count > 0)
                    {
                        var update = new TradeUpdate { Symbol = symbolKey };
                        
                        foreach (var trade in newTrades)
                        {
                            update.Trades.Add(new Trade
                            {
                                Price = (double)trade.Price,
                                Quantity = (double)trade.Quantity,
                                Side = trade.Side,
                                Timestamp = trade.Timestamp.Ticks / TimeSpan.TicksPerMillisecond
                            });
                        }
                        
                        await responseStream.WriteAsync(update);
                    }
                }
                
                lastCheck = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[gRPC] Client disconnected from page {Page}", page);
        }
    }

    /// <summary>
    /// Stream trades in batches (more efficient for high throughput)
    /// </summary>
    public override async Task StreamTradesBatch(
        StreamRequest request,
        IServerStreamWriter<BatchUpdate> responseStream,
        ServerCallContext context)
    {
        // TODO: Implement batch streaming
        // This will be more efficient than individual TradeUpdate messages
        await Task.CompletedTask;
    }
}
