using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpreadAggregator.Domain.Entities;

namespace SpreadAggregator.Infrastructure.Services.Exchanges;

/// <summary>
/// Native WebSocket client for Binance Futures aggTrade stream.
/// Binance Futures WebSocket endpoint: wss://fstream.binance.com/ws
/// Stream format: symbol@aggTrade (e.g., btcusdt@aggTrade)
/// </summary>
public class BinanceFuturesNativeWebSocketClient : IDisposable
{
    private const string WEBSOCKET_ENDPOINT = "wss://fstream.binance.com/ws";
    private const int BUFFER_SIZE = 8192;

    private readonly ConcurrentDictionary<string, Func<TradeData, Task>> _symbolCallbacks = new();
    private readonly ConcurrentDictionary<string, Func<BookTickerData, Task>> _bookTickerCallbacks = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;
    private int _requestId = 1;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Connect to WebSocket endpoint
        await _webSocket.ConnectAsync(new Uri(WEBSOCKET_ENDPOINT), _cts.Token);

        // Start receive loop
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    }

    public async Task SubscribeToTradesAsync(string symbol, Func<TradeData, Task> onTrade, CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
        }

        // Store callback for this symbol
        _symbolCallbacks[symbol] = onTrade;

        // Binance Futures subscription format for aggTrade
        // {"method":"SUBSCRIBE","params":["btcusdt@aggTrade"],"id":1}
        var subscriptionMessage = new
        {
            method = "SUBSCRIBE",
            @params = new[] { $"{symbol.ToLowerInvariant()}@aggTrade" },
            id = Interlocked.Increment(ref _requestId)
        };

        var json = JsonSerializer.Serialize(subscriptionMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        Console.WriteLine($"[BinanceNative] Subscribing to {symbol}: {json}");
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    /// <summary>
    /// Subscribe to bookTicker stream for realtime bid/ask prices (zero latency)
    /// </summary>
    public async Task SubscribeToBookTickersAsync(IEnumerable<string> symbols, Func<BookTickerData, Task> onBookTicker, CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
        }

        var symbolsList = symbols.ToList();
        
        // Store callback for each symbol
        foreach (var symbol in symbolsList)
        {
            _bookTickerCallbacks[symbol] = onBookTicker;
        }

        // Binance Futures subscription format for bookTicker
        // {"method":"SUBSCRIBE","params":["btcusdt@bookTicker","ethusdt@bookTicker"],"id":2}
        var streams = symbolsList.Select(s => $"{s.ToLowerInvariant()}@bookTicker").ToArray();
        var subscriptionMessage = new
        {
            method = "SUBSCRIBE",
            @params = streams,
            id = Interlocked.Increment(ref _requestId)
        };

        var json = JsonSerializer.Serialize(subscriptionMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        Console.WriteLine($"[BinanceNative] Subscribing to bookTicker for {symbolsList.Count} symbols");
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
                    Console.WriteLine($"[BinanceNative] WebSocket state: {_webSocket.State}, exiting receive loop");
                    break;
                }

                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[BinanceNative] WebSocket closed by server: {result.CloseStatusDescription}");
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
                    Console.WriteLine($"[BinanceNative] WebSocket error: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BinanceNative] Receive loop error: {ex.Message}");
        }

        Console.WriteLine($"[BinanceNative] Receive loop ended");
    }

    private async Task ProcessMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Check if it's a subscription response
            if (root.TryGetProperty("result", out _) && root.TryGetProperty("id", out _))
            {
                // Subscription confirmation - can log if needed
                return;
            }

            // Check event type
            if (!root.TryGetProperty("e", out var eventType))
                return;

            var eventTypeName = eventType.GetString();

            // Handle aggTrade messages
            if (eventTypeName == "aggTrade")
            {
                // Parse aggTrade data
                // Format: {"e":"aggTrade","s":"BTCUSDT","p":"43250.50","q":"0.125","T":1701234567890,"m":false}
                if (root.TryGetProperty("s", out var symbolProp) &&
                    root.TryGetProperty("p", out var priceProp) &&
                    root.TryGetProperty("q", out var qtyProp) &&
                    root.TryGetProperty("T", out var timeProp) &&
                    root.TryGetProperty("m", out var isMakerProp))
                {
                    var symbol = symbolProp.GetString();
                    if (string.IsNullOrEmpty(symbol))
                        return;

                    var tradeData = new TradeData
                    {
                        Exchange = "Binance",
                        Symbol = symbol,
                        Price = decimal.Parse(priceProp.GetString()!, CultureInfo.InvariantCulture),
                        Quantity = decimal.Parse(qtyProp.GetString()!, CultureInfo.InvariantCulture),
                        Side = isMakerProp.GetBoolean() ? "Sell" : "Buy",  // Maker = Sell, Taker = Buy
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timeProp.GetInt64()).DateTime
                    };

                    // Call callback if registered
                    if (_symbolCallbacks.TryGetValue(symbol, out var callback))
                    {
                        await callback(tradeData);
                    }
                }
            }
            // Handle bookTicker messages (realtime bid/ask updates)
            else if (eventTypeName == "bookTicker")
            {
                // Parse bookTicker data
                // Format: {"e":"bookTicker","u":400900217,"s":"BTCUSDT","b":"86500.50","B":"1.234","a":"86500.60","A":"5.678"}
                if (root.TryGetProperty("s", out var symbolProp) &&
                    root.TryGetProperty("b", out var bidProp) &&
                    root.TryGetProperty("a", out var askProp))
                {
                    var symbol = symbolProp.GetString();
                    if (string.IsNullOrEmpty(symbol))
                        return;

                    decimal bidQty = 0;
                    decimal askQty = 0;

                    if (root.TryGetProperty("B", out var bidQtyProp))
                        bidQty = decimal.Parse(bidQtyProp.GetString()!, CultureInfo.InvariantCulture);

                    if (root.TryGetProperty("A", out var askQtyProp))
                        askQty = decimal.Parse(askQtyProp.GetString()!, CultureInfo.InvariantCulture);

                    var bookTickerData = new BookTickerData
                    {
                        Exchange = "Binance",
                        Symbol = symbol,
                        BestBid = decimal.Parse(bidProp.GetString()!, CultureInfo.InvariantCulture),
                        BestAsk = decimal.Parse(askProp.GetString()!, CultureInfo.InvariantCulture),
                        BestBidQty = bidQty,
                        BestAskQty = askQty,
                        Timestamp = DateTime.UtcNow
                    };

                    // Call callback if registered
                    if (_bookTickerCallbacks.TryGetValue(symbol, out var callback))
                    {
                        await callback(bookTickerData);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[BinanceNative] JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BinanceNative] Process message error: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            Console.WriteLine($"[BinanceNative] Disconnecting...");
            _cts?.Cancel();

            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BinanceNative] Disconnect error: {ex.Message}");
            }
        }

        // Wait for tasks to complete
        if (_receiveTask != null)
            await _receiveTask;

        Console.WriteLine($"[BinanceNative] Disconnected");
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
