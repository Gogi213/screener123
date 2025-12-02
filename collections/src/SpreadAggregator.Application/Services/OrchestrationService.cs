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
    private readonly TradeAggregatorService _tradeAggregator;

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
        TradeAggregatorService tradeAggregator)
    {
        _webSocketServer = webSocketServer;
        _configuration = configuration;
        _volumeFilter = volumeFilter;
        _exchangeClients = exchangeClients;
        _tradeScreenerChannel = tradeScreenerChannel;
        _tradeAggregator = tradeAggregator;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // MEXC TRADES VIEWER: WebSocket server enabled for real-time trade streaming
        _webSocketServer.Start();

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
                    // FUTURES_FIX: Use exchangeClient.ExchangeName to match keys in TradeData
                    await ProcessExchange(exchangeClient, exchangeClient.ExchangeName, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"[{exchangeClient.ExchangeName}] Exchange stopped gracefully");
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

        Console.WriteLine($"[{exchangeName}] Loading symbols...");
        var allSymbols = (await exchangeClient.GetSymbolsAsync()).ToList();
        Console.WriteLine($"[{exchangeName}] ✅ Loaded {allSymbols.Count} symbols");

        // PROPOSAL-2025-0095: Thread-safe symbol addition
        lock (_symbolLock)
        {
            var existingSymbols = new HashSet<string>(_allSymbolInfo.Select(s => s.Name));
            var newSymbols = allSymbols.Where(s => !existingSymbols.Contains(s.Name)).ToList();
            _allSymbolInfo.AddRange(newSymbols);
        }

        Console.WriteLine($"[{exchangeName}] Loading tickers...");
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

        // BLACKLIST: Remove major coins (except deviation analysis symbols: BTC, ETH, SOL, DOGE, SUI, LINK)
        var blacklistBases = new[] { "XRP", "PEPE", "BNB", "TAO", "SHIB", "LTC", "AVAX", "DOT" };
        var beforeBlacklist = filteredSymbolNames.Count;
        filteredSymbolNames = filteredSymbolNames
            .Where(symbol => !blacklistBases.Any(base_ =>
                symbol.StartsWith(base_, StringComparison.OrdinalIgnoreCase) ||
                symbol.StartsWith(base_ + "_", StringComparison.OrdinalIgnoreCase) ||
                symbol.StartsWith(base_ + "-", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (beforeBlacklist > filteredSymbolNames.Count)
        {
            Console.WriteLine($"[{exchangeName}] Blacklist removed {beforeBlacklist - filteredSymbolNames.Count} major coins (XRP/DOGE/ETH/BTC/SOL/PEPE)");
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
                   Console.WriteLine($"[Orchestration-WARN] Trade screener channel full (system overload), dropping trade data");
                }
                return Task.CompletedTask;
            }));
        }

        // SPRINT-1.1: Subscribe to bookTicker stream for realtime bid/ask (Binance only)
        if (exchangeName.Equals("Binance", StringComparison.OrdinalIgnoreCase))
        {
            // Check if this is BinanceFuturesExchangeClient (has native WebSocket with SubscribeToBookTickersAsync)
            var binanceClient = exchangeClient as BinanceFuturesExchangeClient;
            if (binanceClient != null)
            {
                Console.WriteLine($"[{exchangeName}] Adding bookTicker subscription for realtime bid/ask...");
                // Note: We don't await this, it runs in background with trade subscription
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Subscribe using BinanceFuturesExchangeClient's method
                        await binanceClient.SubscribeToBookTickersAsync(filteredSymbolNames, bookTickerData =>
                        {
                            _tradeAggregator.UpdateBookTickerData(bookTickerData, exchangeName);
                            return Task.CompletedTask;
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{exchangeName}] BookTicker subscription error: {ex.Message}");
                    }
                }, cancellationToken);
            }
        }


        Console.WriteLine($"[{exchangeName}] Subscription tasks started (running in background)...");

        // SPRINT-10: Periodic ticker data refresh (Volume24h, PriceChangePercent24h)
        var tickerTimer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(10));
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await tickerTimer.WaitForNextTickAsync(cancellationToken);
                    var freshTickers = await exchangeClient.GetTickersAsync();
                    _tradeAggregator.UpdateTickerData(freshTickers, exchangeName);
                    
                    // Note: BestBid/BestAsk now updated realtime via bookTicker WebSocket (see above)
                    // No need for 10-second polling anymore!
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{exchangeName}] Ticker refresh error: {ex.Message}");
                }
            }
            tickerTimer.Dispose();
        }, cancellationToken);

        // SPRINT-12: Periodic orderbook refresh for MEXC Futures (spread calculation)
        // High activity symbols: every 10 seconds
        // Low activity symbols: every 60 seconds
        if (exchangeName.Equals("MexcFutures", StringComparison.OrdinalIgnoreCase))
        {
            // High activity timer (10 seconds)
            var highActivityTimer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(10));
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await highActivityTimer.WaitForNextTickAsync(cancellationToken);

                        var (highActivity, _) = _tradeAggregator.GetActiveSymbolsByActivity();
                        if (highActivity.Any() && exchangeClient.GetType().Name == "MexcFuturesExchangeClient")
                        {
                            var method = exchangeClient.GetType().GetMethod("GetOrderbookForSymbolsAsync");
                            if (method != null)
                            {
                                var task = (Task<Dictionary<string, (decimal, decimal)>>)method.Invoke(exchangeClient, new object[] { highActivity });
                                var orderbookData = await task;
                                _tradeAggregator.UpdateOrderbookData(orderbookData, exchangeName);
                                Console.WriteLine($"[{exchangeName}] High-activity orderbook refreshed: {orderbookData.Count} symbols (10s interval)");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{exchangeName}] High-activity orderbook refresh error: {ex.Message}");
                    }
                }
                highActivityTimer.Dispose();
            }, cancellationToken);

            // Low activity timer (60 seconds)
            var lowActivityTimer = new System.Threading.PeriodicTimer(TimeSpan.FromSeconds(60));
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await lowActivityTimer.WaitForNextTickAsync(cancellationToken);

                        var (_, lowActivity) = _tradeAggregator.GetActiveSymbolsByActivity();
                        if (lowActivity.Any() && exchangeClient.GetType().Name == "MexcFuturesExchangeClient")
                        {
                            var method = exchangeClient.GetType().GetMethod("GetOrderbookForSymbolsAsync");
                            if (method != null)
                            {
                                var task = (Task<Dictionary<string, (decimal, decimal)>>)method.Invoke(exchangeClient, new object[] { lowActivity });
                                var orderbookData = await task;
                                _tradeAggregator.UpdateOrderbookData(orderbookData, exchangeName);
                                Console.WriteLine($"[{exchangeName}] Low-activity orderbook refreshed: {orderbookData.Count} symbols (60s interval)");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{exchangeName}] Low-activity orderbook refresh error: {ex.Message}");
                    }
                }
                lowActivityTimer.Dispose();
            }, cancellationToken);
        }

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
