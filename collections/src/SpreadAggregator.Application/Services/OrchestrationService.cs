using Microsoft.Extensions.Configuration;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Services;

public class OrchestrationService
{
    private readonly IWebSocketServer _webSocketServer;
    private readonly VolumeFilter _volumeFilter;
    private readonly IConfiguration _configuration;
    private readonly IEnumerable<IExchangeClient> _exchangeClients;
    private readonly Channel<MarketData> _tradeScreenerChannel;
    private readonly BinanceSpotFilter _binanceSpotFilter;

    // PROPOSAL-2025-0095: Track symbols and tasks for cleanup
    private readonly List<SymbolInfo> _allSymbolInfo = new();
    private readonly List<Task> _exchangeTasks = new();
    private readonly object _symbolLock = new();
    private readonly object _taskLock = new();
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// PROPOSAL-2025-0095: Get exchange health status for monitoring
    /// </summary>
    public Dictionary<string, string> GetExchangeHealth()
    {
        lock (_taskLock)
        {
            var exchangeNames = _configuration.GetSection("ExchangeSettings:Exchanges").GetChildren().Select(x => x.Key);
            var health = new Dictionary<string, string>();

            foreach (var exchangeName in exchangeNames)
            {
                var task = _exchangeTasks.FirstOrDefault();
                if (task == null)
                {
                    health[exchangeName] = "not_started";
                }
                else if (task.IsFaulted)
                {
                    health[exchangeName] = "failed";
                }
                else if (task.IsCompleted)
                {
                    health[exchangeName] = "stopped";
                }
                else
                {
                    health[exchangeName] = "running";
                }
            }

            return health;
        }
    }

    public OrchestrationService(
        IWebSocketServer webSocketServer,
        IConfiguration configuration,
        VolumeFilter volumeFilter,
        IEnumerable<IExchangeClient> exchangeClients,
        Channel<MarketData> tradeScreenerChannel,
        BinanceSpotFilter binanceSpotFilter)
    {
        _webSocketServer = webSocketServer;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _tradeScreenerChannel = tradeScreenerChannel;
        _binanceSpotFilter = binanceSpotFilter;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // MEXC TRADES VIEWER: WebSocket server enabled for real-time trade streaming
        _webSocketServer.Start();

        // Load Binance Spot symbols for filtering
        await _binanceSpotFilter.LoadAsync();

        // Create cancellation token source for stopping all exchanges
        _cancellationTokenSource = new CancellationTokenSource();

        var exchangeNames = _configuration.GetSection("ExchangeSettings:Exchanges").GetChildren().Select(x => x.Key);
        var tasks = new List<Task>();

        foreach (var exchangeName in exchangeNames)
        {
            var exchangeClient = _exchangeClients.FirstOrDefault(c => c.ExchangeName.Equals(exchangeName, StringComparison.OrdinalIgnoreCase));
            if (exchangeClient == null)
            {
                Console.WriteLine($"[ERROR] No client found for exchange: {exchangeName}");
                continue;
            }

            // PROPOSAL-2025-0095: Error boundary - one exchange failure doesn't crash entire system
            var task = Task.Run(async () =>
            {
                try
                {
                    await ProcessExchange(exchangeClient, exchangeName, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"[{exchangeName}] Exchange stopped gracefully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FATAL] [{exchangeName}] Exchange failed with error: {ex.Message}");
                    Console.WriteLine($"[INFO] [{exchangeName}] Other exchanges continue running");
                }
            }, _cancellationTokenSource.Token);

            tasks.Add(task);

            // PROPOSAL-2025-0095: Thread-safe task tracking
            lock (_taskLock)
            {
                _exchangeTasks.Add(task); // Store for monitoring/cleanup
            }
        }

        // Store tasks but don't await - they are long-running background subscriptions
        Console.WriteLine($"[Orchestration] Started {_exchangeTasks.Count} exchange subscription tasks");
    }

    private async Task ProcessExchange(IExchangeClient exchangeClient, string exchangeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exchangeConfig = _configuration.GetSection($"ExchangeSettings:Exchanges:{exchangeName}:VolumeFilter");
        var minVolume = exchangeConfig.GetValue<decimal?>("MinUsdVolume") ?? 0;
        var maxVolume = exchangeConfig.GetValue<decimal?>("MaxUsdVolume") ?? decimal.MaxValue;

        var allSymbols = (await exchangeClient.GetSymbolsAsync()).ToList();

        // PROPOSAL-2025-0095: Thread-safe symbol addition
        lock (_symbolLock)
        {
            var existingSymbols = new HashSet<string>(_allSymbolInfo.Select(s => s.Name));
            var newSymbols = allSymbols.Where(s => !existingSymbols.Contains(s.Name)).ToList();
            _allSymbolInfo.AddRange(newSymbols);
        }
        
        var tickers = (await exchangeClient.GetTickersAsync()).ToList();
        Console.WriteLine($"[{exchangeName}] Received {tickers.Count} tickers and {allSymbols.Count} symbol info objects.");

        var tickerLookup = tickers.ToDictionary(t => t.Symbol, t => t.QuoteVolume);

        // STRESS TEST: Removed USDT/USDC filter - now subscribes to ALL pairs (BTC, ETH, USDT, USDC, etc.)
        var filteredSymbolInfo = allSymbols
            .Where(s => tickerLookup.ContainsKey(s.Name) &&
                        _volumeFilter.IsVolumeSufficient(tickerLookup[s.Name], minVolume, maxVolume))
            .ToList();
        
        var filteredSymbolNames = filteredSymbolInfo.Select(s => s.Name).ToList();
        
        Console.WriteLine($"[{exchangeName}] {filteredSymbolNames.Count} symbols passed the volume filter.");

        // Apply Binance filter (only for MEXC exchange)
        if (exchangeName.Equals("MEXC", StringComparison.OrdinalIgnoreCase))
        {
            filteredSymbolNames = _binanceSpotFilter.FilterExcludeBinance(filteredSymbolNames);
        }

        if (!filteredSymbolNames.Any())
        {
            Console.WriteLine($"[{exchangeName}] No symbols to subscribe to after filtering.");
            return;
        }

        var tasks = new List<Task>();
        var enableTrades = _configuration.GetValue<bool>("StreamSettings:EnableTrades", true);

        if (enableTrades)
        {
            Console.WriteLine($"[{exchangeName}] Adding trade subscription task...");
            tasks.Add(exchangeClient.SubscribeToTradesAsync(filteredSymbolNames, tradeData =>
            {
                // MEXC TRADES VIEWER: Write trades to TradeScreenerChannel for TradeAggregatorService
                if (!_tradeScreenerChannel.Writer.TryWrite(tradeData))
                {
                   // Console.WriteLine($"[Orchestration-WARN] Trade screener channel full (system overload), dropping trade data");
                }
                return Task.CompletedTask;
            }));
        }


        Console.WriteLine($"[{exchangeName}] Subscription tasks started (running in background)...");

        // MEXC TRADES VIEWER: Subscriptions must stay alive until cancellation
        // Tasks run in background and keep the stream alive
        // Wait indefinitely until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{exchangeName}] Exchange subscriptions cancelled gracefully");
        }

        Console.WriteLine($"[{exchangeName}] All subscription tasks completed");

    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Orchestration] Stopping {_exchangeTasks.Count} exchange tasks...");

        // Step 1: Cancel all operations
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
        {
            Console.WriteLine("[Orchestration] Cancelling all operations...");
            _cancellationTokenSource.Cancel();
        }

        // Step 2: Stop all exchange client connections explicitly
        Console.WriteLine("[Orchestration] Stopping all exchange client connections...");
        var stopClientTasks = _exchangeClients.Select(client =>
        {
            try
            {
                return client.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Orchestration] Error stopping {client.ExchangeName}: {ex.Message}");
                return Task.CompletedTask;
            }
        }).ToArray();

        await Task.WhenAll(stopClientTasks);
        Console.WriteLine("[Orchestration] All exchange clients stopped");

        // Step 3: Wait for tasks to complete
        Task[] tasksSnapshot;
        lock (_taskLock)
        {
            // PROPOSAL-2025-0095: Cleanup completed tasks before shutdown
            _exchangeTasks.RemoveAll(t => t.IsCompleted);
            tasksSnapshot = _exchangeTasks.ToArray();
        }

        if (tasksSnapshot.Length == 0)
        {
            Console.WriteLine("[Orchestration] No active tasks to stop");
            return;
        }

        // Give tasks a chance to complete gracefully
        var completedTask = await Task.WhenAny(Task.WhenAll(tasksSnapshot), Task.Delay(2000, cancellationToken));

        if (completedTask == Task.WhenAll(tasksSnapshot))
        {
            Console.WriteLine("[Orchestration] All tasks stopped gracefully");
        }
        else
        {
            Console.WriteLine("[Orchestration] Tasks did not complete in 2 seconds, forcing shutdown");
        }

        // Dispose cancellation token source
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // PROPOSAL-2025-0095: Clear task list
        lock (_taskLock)
        {
            _exchangeTasks.Clear();
        }
    }

}
