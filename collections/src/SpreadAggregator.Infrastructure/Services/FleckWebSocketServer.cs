using Fleck;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

/// <summary>
/// WebSocket server implementation using Fleck library.
/// Note: IWebSocketServer abstraction maintained for proper layered architecture (Application <- Infrastructure).
/// </summary>
public class FleckWebSocketServer : Application.Abstractions.IWebSocketServer, IDisposable
{
    private readonly WebSocketServer _server;
    private readonly List<IWebSocketConnection> _allSockets;
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly ConcurrentDictionary<Guid, int> _clientSubscriptions = new();
    private TradeAggregatorService? _tradeAggregatorService;

    public FleckWebSocketServer(string location)
    {
        _server = new WebSocketServer(location);
        _allSockets = new List<IWebSocketConnection>();
        _cleanupTimer = new System.Threading.Timer(CleanupDeadConnections, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

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
                _rwLock.EnterWriteLock();
                try
                {
                    Console.WriteLine($"[Fleck] Client connected: {socket.ConnectionInfo.ClientIpAddress}");
                    _allSockets.Add(socket);
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }

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
                            lastUpdate = m.LastUpdate.ToString("o"),
                            tradesPerMin = m.TradesPerMin
                        })
                    };
                    var json = JsonSerializer.Serialize(response);
                    socket.Send(json);
                }
            };

            socket.OnClose = () =>
            {
                _rwLock.EnterWriteLock();
                try
                {
                    _allSockets.Remove(socket);
                    _clientSubscriptions.TryRemove(socket.ConnectionInfo.Id, out _);
                    Console.WriteLine($"[Fleck] Client disconnected.");
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            };

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
                        _clientSubscriptions[socket.ConnectionInfo.Id] = page;

                        if (_tradeAggregatorService != null)
                        {
                            // SPRINT-R4: Removed intermediate .ToList() calls - evaluate lazily
                            var symbolsOnPage = _tradeAggregatorService.GetAllSymbolsMetadata()
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize)
                                .Select(m => $"MEXC_{m.Symbol}")
                                .ToList(); // Final ToList() needed for GetTradesForSymbols()

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

                        Console.WriteLine($"[Fleck] Client subscribed to page {page}");
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
        
        _rwLock.EnterReadLock();
        try
        {
            socketsSnapshot = _allSockets.ToList();
        }
        finally
        {
            _rwLock.ExitReadLock();
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
                    Console.WriteLine($"[Fleck] Error sending: {ex.Message}");
                }
            }
        }
        return Task.WhenAll(tasks);
    }

    private void CleanupDeadConnections(object? state)
    {
        _rwLock.EnterWriteLock();
        try
        {
            var deadConnections = _allSockets.Where(s => !s.IsAvailable).ToList();
            foreach (var socket in deadConnections)
            {
                _allSockets.Remove(socket);
            }
            if (deadConnections.Count > 0)
            {
                Console.WriteLine($"[Fleck] Cleaned up {deadConnections.Count} dead connections.");
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _rwLock?.Dispose();
        _cleanupTimer?.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}