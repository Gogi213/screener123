using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpreadAggregator.Domain.Entities;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Native WebSocket client for MEXC Futures push.deal channel.
/// MEXC Futures WebSocket endpoint: wss://wbs.mexc.com/ws
/// Channel: push.deal (real-time trades)
/// </summary>
public class MexcFuturesNativeWebSocketClient : IDisposable
{
    private const string WEBSOCKET_ENDPOINT = "wss://contract.mexc.com/edge";
    private const int PING_INTERVAL_MS = 30000; // 30 seconds (MEXC requires ping every 60s)
    private const int BUFFER_SIZE = 8192;

    private readonly ConcurrentDictionary<string, Func<TradeData, Task>> _symbolCallbacks = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _pingTask;
    private bool _disposed;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Connect to WebSocket endpoint
        await _webSocket.ConnectAsync(new Uri(WEBSOCKET_ENDPOINT), _cts.Token);

        // Start receive loop
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);

        // Start ping loop
        _pingTask = Task.Run(() => PingLoop(_cts.Token), _cts.Token);
    }

    public async Task SubscribeToTradesAsync(string symbol, Func<TradeData, Task> onTrade, CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
        }

        // Store callback for this symbol
        _symbolCallbacks[symbol] = onTrade;

        // MEXC Futures subscription format for push.deal
        // {"method":"sub.deal","param":{"symbol":"BTC_USDT"}}
        var subscriptionMessage = new
        {
            method = "sub.deal",
            param = new { symbol }
        };

        var json = JsonSerializer.Serialize(subscriptionMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        Console.WriteLine($"[MexcFuturesNative] Subscribing to {symbol}: {json}");
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[BUFFER_SIZE];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket != null)
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    Console.WriteLine($"[MexcFuturesNative] WebSocket state: {_webSocket.State}, exiting receive loop");
                    break;
                }

                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[MexcFuturesNative] WebSocket closed by server: {result.CloseStatusDescription}");
                        break;
                    }

                    var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuilder.Append(chunk);

                    // Complete message received
                    if (result.EndOfMessage)
                    {
                        var message = messageBuilder.ToString();
                        messageBuilder.Clear();

                        // Process message
                        await ProcessMessage(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Console.WriteLine($"[MexcFuturesNative] WebSocket error: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MexcFuturesNative] Receive loop error: {ex.Message}");
        }

        Console.WriteLine($"[MexcFuturesNative] Receive loop ended");
    }

    private async Task ProcessMessage(string message)
    {
        try
        {
            // Process message without debug logging

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Check if it's a ping message
            if (root.TryGetProperty("channel", out var channel) && channel.GetString() == "ping")
            {
                // Respond with pong
                await SendPongAsync();
                return;
            }

            // Check if it's a trade data message (push.deal)
            if (root.TryGetProperty("channel", out var dataChannel) && dataChannel.GetString() == "push.deal")
            {
                if (root.TryGetProperty("symbol", out var symbolProp) &&
                    root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    var symbol = symbolProp.GetString();
                    if (string.IsNullOrEmpty(symbol))
                        return;

                    // Parse trade data array
                    foreach (var tradeElem in data.EnumerateArray())
                    {
                        // Format: {p: price, v: volume, T: trade_type (1=buy, 2=sell), t: timestamp}
                        if (tradeElem.TryGetProperty("p", out var priceProp) &&
                            tradeElem.TryGetProperty("v", out var volumeProp) &&
                            tradeElem.TryGetProperty("T", out var tradeProp) &&
                            tradeElem.TryGetProperty("t", out var timestampProp))
                        {
                            var tradeData = new TradeData
                            {
                                Exchange = "MexcFutures",
                                Symbol = symbol,
                                Price = priceProp.GetDecimal(),
                                Quantity = volumeProp.GetDecimal(),
                                Side = tradeProp.GetInt32() == 1 ? "Buy" : "Sell",
                                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampProp.GetInt64()).DateTime
                            };

                            // Call callback if registered
                            if (_symbolCallbacks.TryGetValue(symbol, out var callback))
                            {
                                await callback(tradeData);
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[MexcFuturesNative] JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MexcFuturesNative] Process message error: {ex.Message}");
        }
    }

    private async Task PingLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket != null)
            {
                await Task.Delay(PING_INTERVAL_MS, cancellationToken);

                if (_webSocket.State == WebSocketState.Open)
                {
                    // Send ping message: {"method":"ping"}
                    var pingMessage = "{\"method\":\"ping\"}";
                    var bytes = Encoding.UTF8.GetBytes(pingMessage);
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
                    Console.WriteLine($"[MexcFuturesNative] Ping sent");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MexcFuturesNative] Ping loop error: {ex.Message}");
        }
    }

    private async Task SendPongAsync()
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            var pongMessage = "{\"method\":\"pong\"}";
            var bytes = Encoding.UTF8.GetBytes(pongMessage);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"[MexcFuturesNative] Pong sent");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            Console.WriteLine($"[MexcFuturesNative] Disconnecting...");
            _cts?.Cancel();

            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MexcFuturesNative] Disconnect error: {ex.Message}");
            }
        }

        // Wait for tasks to complete
        if (_receiveTask != null)
            await _receiveTask;
        if (_pingTask != null)
            await _pingTask;

        Console.WriteLine($"[MexcFuturesNative] Disconnected");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
    }
}
