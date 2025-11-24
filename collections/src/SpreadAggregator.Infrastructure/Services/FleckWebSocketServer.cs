using Fleck;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

public class FleckWebSocketServer : Application.Abstractions.IWebSocketServer, IDisposable
{
    private readonly WebSocketServer _server;
    private readonly List<IWebSocketConnection> _allSockets;
    private readonly object _lock = new object();
    private readonly Func<OrchestrationService> _orchestrationServiceFactory;
    private readonly System.Threading.Timer _cleanupTimer;

    // MEXC TRADES VIEWER: Track client subscriptions (ConnectionId → PageNumber)
    private readonly ConcurrentDictionary<Guid, int> _clientSubscriptions = new();

    // MEXC TRADES VIEWER: TradeAggregatorService instance (injected after construction)
    private TradeAggregatorService? _tradeAggregatorService;

    public FleckWebSocketServer(string location, Func<OrchestrationService> orchestrationServiceFactory)
    {
        _server = new WebSocketServer(location);
        _allSockets = new List<IWebSocketConnection>();
        _orchestrationServiceFactory = orchestrationServiceFactory;

        // PROPOSAL-2025-0095: Dead connection cleanup every 5 minutes
        _cleanupTimer = new System.Threading.Timer(CleanupDeadConnections, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// MEXC TRADES VIEWER: Inject TradeAggregatorService for metadata queries
    /// </summary>
    public void SetTradeAggregatorService(TradeAggregatorService service)
    {
        _tradeAggregatorService = service;
    }

    public void Start()
    {
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                lock (_lock)
                {
                    Console.WriteLine($"[Fleck] Client connected: {socket.ConnectionInfo.ClientIpAddress}");
                    _allSockets.Add(socket);
                }

                // MEXC TRADES VIEWER: Send symbol metadata on connect
                if (_tradeAggregatorService != null)
                {
                    var metadata = _tradeAggregatorService.GetAllSymbolsMetadata();
                    var response = new
                    {
                        type = "symbols_metadata",
                        total_symbols = metadata.Count(),
                        symbols = metadata.Select(m => new
                        {
                            symbol = m.Symbol,
                            lastPrice = m.LastPrice,
                            lastUpdate = m.LastUpdate.ToString("o")
                        })
                    };
                    var json = JsonSerializer.Serialize(response);
                    socket.Send(json);
                }
            };

            socket.OnClose = () =>
            {
                lock (_lock)
                {
                    _allSockets.Remove(socket);
                    _clientSubscriptions.TryRemove(socket.ConnectionInfo.Id, out _);
                    Console.WriteLine($"[Fleck] Client disconnected.");
                }
            };

            // MEXC TRADES VIEWER: Handle client messages (subscribe_page, etc.)
            socket.OnMessage = async message =>
            {
                try
                {
                    var request = JsonSerializer.Deserialize<ClientRequest>(message);
                    if (request == null) return;

                    if (request.action == "subscribe_page")
                    {
                        var page = request.page ?? 1;
                        var pageSize = request.page_size ?? 100;

                        // Store subscription
                        _clientSubscriptions[socket.ConnectionInfo.Id] = page;

                        // Send initial data for this page
                        if (_tradeAggregatorService != null)
                        {
                            var allMetadata = _tradeAggregatorService.GetAllSymbolsMetadata().ToList();
                            var symbolsOnPage = allMetadata
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize)
                                .Select(m => $"MEXC_{m.Symbol}")
                                .ToList();

                            var tradesData = _tradeAggregatorService.GetTradesForSymbols(symbolsOnPage);

                            var response = new
                            {
                                type = "page_data",
                                page,
                                symbols = tradesData.Select(kvp => new
                                {
                                    symbol = kvp.Key,
                                    trades = kvp.Value.Select(t => new
                                    {
                                        price = t.Price,
                                        quantity = t.Quantity,
                                        side = t.Side,
                                        timestamp = t.Timestamp.ToString("o")
                                    })
                                })
                            };

                            var json = JsonSerializer.Serialize(response);
                            await socket.Send(json);
                        }

                        Console.WriteLine($"[Fleck] Client {socket.ConnectionInfo.ClientIpAddress} subscribed to page {page}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fleck] Error handling message: {ex.Message}");
                }
            };
        });
    }

    private class ClientRequest
    {
        public string? action { get; set; }
        public int? page { get; set; }
        public int? page_size { get; set; }
    }

    public Task BroadcastRealtimeAsync(string message)
    {
        List<IWebSocketConnection> socketsSnapshot;
        lock (_lock)
        {
           // Take a snapshot to avoid holding the lock during I/O operations
           socketsSnapshot = _allSockets.ToList();
        }

        var tasks = new List<Task>();
        foreach (var socket in socketsSnapshot)
        {
            if (socket.IsAvailable)
            {
                try
                {
                    tasks.Add(socket.Send(message));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fleck] Error sending to socket: {ex.Message}. Client might have disconnected.");
                }
            }
        }
        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// PROPOSAL-2025-0095: Cleanup dead WebSocket connections
    /// Removes connections that are closed but not properly removed from list
    /// </summary>
    private void CleanupDeadConnections(object? state)
    {
        lock (_lock)
        {
            var deadConnections = _allSockets
                .Where(s => !s.IsAvailable)
                .ToList();

            foreach (var socket in deadConnections)
            {
                _allSockets.Remove(socket);
            }

            if (deadConnections.Count > 0)
            {
                Console.WriteLine($"[Fleck] Cleaned up {deadConnections.Count} dead connections. Active: {_allSockets.Count}");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}