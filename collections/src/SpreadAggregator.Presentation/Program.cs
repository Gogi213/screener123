using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Presentation.Diagnostics;
using SpreadAggregator.Domain.Entities;
using SpreadAggregator.Domain.Services;
using SpreadAggregator.Infrastructure.Services;
using SpreadAggregator.Infrastructure.Services.Exchanges;
using System;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using SpreadAggregator.Application.Diagnostics;

namespace SpreadAggregator.Presentation;

public class TradeScreenerChannel
{
    public Channel<MarketData> Channel { get; }
    public TradeScreenerChannel(Channel<MarketData> channel) => Channel = channel;
}

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        // Configure application services
        ConfigureServices(builder.Services, builder.Configuration);

        // Add ASP.NET Core services for Charts API
        builder.Services.AddControllers();
        
        // PHASE-2-FIX-6: Health Check endpoint for monitoring
        builder.Services.AddHealthChecks()
            .AddCheck<MexcHealthCheck>("mexc_websocket");
        
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // Configure middleware
        app.UseStaticFiles(); // Serve static files from wwwroot
        app.UseRouting(); // Enable routing
        app.UseWebSockets();
        app.UseCors();
        app.MapControllers();
        
        // PHASE-2-FIX-6: Health Check endpoint
        app.MapHealthChecks("/health");

        // Start background services
        await app.RunAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IWebSocketServer>(sp =>
        {
            var connectionString = configuration.GetSection("ConnectionStrings")?["WebSocket"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("WebSocket connection string is not configured.");
            }
            return new FleckWebSocketServer(connectionString);
        });

        services.AddSingleton<VolumeFilter>();

        var channelOptions = new BoundedChannelOptions(1_000_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        };

        var tradeScreenerChannel = Channel.CreateBounded<MarketData>(channelOptions);
        services.AddSingleton<TradeScreenerChannel>(new TradeScreenerChannel(tradeScreenerChannel));

        // Register all exchange clients
        // MEXC TRADES VIEWER: Only MEXC enabled
        services.AddSingleton<IExchangeClient, MexcExchangeClient>();

        // GEMINI_DEV: Enable centralized performance monitoring
        services.AddSingleton<PerformanceMonitor>(sp =>
        {
            var logDir = "./data/performance";
            return new PerformanceMonitor(logDir);
        });

        // MEXC TRADES VIEWER: TradeAggregatorService - processes trades from TradeScreenerChannel
        services.AddSingleton<TradeAggregatorService>(sp =>
        {
            var tradeChannel = sp.GetRequiredService<TradeScreenerChannel>().Channel;
            var webSocketServer = sp.GetRequiredService<IWebSocketServer>();
            var logger = sp.GetRequiredService<ILogger<TradeAggregatorService>>();
            var perfMonitor = sp.GetRequiredService<PerformanceMonitor>();
            return new TradeAggregatorService(tradeChannel, webSocketServer, logger, perfMonitor);
        });

        services.AddSingleton<OrchestrationService>(sp =>
        {
            return new OrchestrationService(
                sp.GetRequiredService<IWebSocketServer>(),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<VolumeFilter>(),
                sp.GetRequiredService<IEnumerable<IExchangeClient>>(),
                sp.GetRequiredService<TradeScreenerChannel>().Channel
            );
        });

        services.AddHostedService<OrchestrationServiceHost>();
        // MEXC TRADES VIEWER: TradeAggregatorServiceHost - processes trades
        services.AddHostedService<TradeAggregatorServiceHost>();
    }
}

public class OrchestrationServiceHost : IHostedService
{
    private readonly OrchestrationService _orchestrationService;
    private readonly ILogger<OrchestrationServiceHost> _logger;

    public OrchestrationServiceHost(
        OrchestrationService orchestrationService,
        ILogger<OrchestrationServiceHost> logger)
    {
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[OrchestrationHost] Starting orchestration service...");
        _ = _orchestrationService.StartAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // PROPOSAL-2025-0095: Graceful shutdown
        _logger.LogInformation("[OrchestrationHost] Stopping orchestration service gracefully...");

        // Stop orchestration (stops exchange subscriptions)
        await _orchestrationService.StopAsync(cancellationToken);

        _logger.LogInformation("[OrchestrationHost] Orchestration service stopped");
    }
}

public class TradeAggregatorServiceHost : IHostedService
{
    private readonly TradeAggregatorService _tradeAggregatorService;
    private readonly TradeScreenerChannel _tradeScreenerChannel;
    private readonly IWebSocketServer _webSocketServer;
    private readonly ILogger<TradeAggregatorServiceHost> _logger;
    private Task? _runningTask;
    private CancellationTokenSource? _cts;

    public TradeAggregatorServiceHost(
        TradeAggregatorService tradeAggregatorService,
        TradeScreenerChannel tradeScreenerChannel,
        IWebSocketServer webSocketServer,
        ILogger<TradeAggregatorServiceHost> logger)
    {
        _tradeAggregatorService = tradeAggregatorService;
        _tradeScreenerChannel = tradeScreenerChannel;
        _webSocketServer = webSocketServer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TradeAggregatorHost] Starting trade aggregator service...");

        // MEXC TRADES VIEWER: Inject TradeAggregatorService into WebSocketServer
        if (_webSocketServer is FleckWebSocketServer fleckServer)
        {
            fleckServer.SetTradeAggregatorService(_tradeAggregatorService);
            _logger.LogInformation("[TradeAggregatorHost] Injected TradeAggregatorService into WebSocketServer");
        }

        _cts = new CancellationTokenSource();
        _runningTask = _tradeAggregatorService.StartAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[TradeAggregatorHost] Stopping trade aggregator service gracefully...");

        // Signal cancellation
        _cts?.Cancel();

        // Complete the channel to stop processing
        _tradeScreenerChannel.Channel.Writer.Complete();

        // Wait for task to finish (with timeout)
        if (_runningTask != null)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completed = await Task.WhenAny(_runningTask, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("[TradeAggregatorHost] Trade aggregator service did not stop within 5 seconds");
            }
            else
            {
                _logger.LogInformation("[TradeAggregatorHost] Trade aggregator service stopped");
            }
        }

        _cts?.Dispose();
    }
}
