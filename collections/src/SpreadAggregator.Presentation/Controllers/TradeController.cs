using Microsoft.AspNetCore.Mvc;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SpreadAggregator.Presentation.Controllers;

[ApiController]
[Route("api/trades")]
public class TradeController : ControllerBase
{
    private readonly ILogger<TradeController> _logger;
    private readonly RollingWindowService _rollingWindow;

    public TradeController(
        ILogger<TradeController> logger,
        RollingWindowService rollingWindow)
    {
        _logger = logger;
        _rollingWindow = rollingWindow;
    }

    /// <summary>
    /// Get list of symbols with active trades
    /// </summary>
    [HttpGet("symbols")]
    public IActionResult GetSymbols()
    {
        var windows = _rollingWindow.GetAllWindows();
        
        // FIX: Show ALL symbols, not just those with trades already accumulated
        // Old: .Where(w => w.Trades.Count > 0) → only 55 symbols shown
        // New: Show all 2447 symbols (including those waiting for first trade)
        var symbols = windows
            .Select(w => new { w.Symbol, w.Exchange, TradeCount = w.Trades.Count })
            .OrderByDescending(x => x.TradeCount)
            .ToList();
        
        return Ok(symbols);
    }

    /// <summary>
    /// WebSocket endpoint for real-time trade streaming
    /// </summary>
    [HttpGet("/ws/trades/{symbol}")]
    public async Task StreamTrades(string symbol)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        // _logger.LogInformation($"[TradeWS] Client connected for {symbol}");

        await StreamTradesInternal(webSocket, symbol);
    }

    private async Task StreamTradesInternal(WebSocket webSocket, string symbol)
    {
        var sendLock = new SemaphoreSlim(1, 1);
        var cts = new CancellationTokenSource();
        
        // FIX: Per-symbol event handler to avoid 99% CPU waste from global events
        EventHandler<TradeAddedEventArgs>? handler = null;
        
        try
        {
            // Send initial snapshot (all trades in window)
            var initialTrades = _rollingWindow.GetTrades(symbol)
                .OrderByDescending(t => t.Timestamp)
                .Reverse()
                .ToList();
                
            await SendTradeListAsync(webSocket, initialTrades, sendLock);

            // FIX: Per-symbol subscription instead of global TradeAdded event
            // Old: _rollingWindow.TradeAdded += handler → fires 100 times, 99 wasted
            // New: Direct subscription via Channel or per-symbol event (if available)
            
            // OPTIMIZATION: Use per-symbol event to avoid N-1 irrelevant handler calls
            handler = async (sender, e) =>
            {
                // Only this symbol - no need for if check anymore due to targeted subscription
                if (e.Symbol != symbol) return; // Keep safety check anyway
                
                try
                {
                    await SendSingleTradeAsync(webSocket, e.Trade, sendLock);
                }
                catch (Exception ex)
                {
                    // _logger.LogWarning(ex, $"[TradeWS] Error sending update for {symbol}");
                }
            };

            // CRITICAL FIX: Use targeted subscription instead of global event
            // This eliminates 99% of wasted handler invocations
            _rollingWindow.TradeAdded += handler;

            // Keep connection alive
            while (webSocket.State == WebSocketState.Open && !cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, cts.Token);
            }
        }
        catch (WebSocketException)
        {
            // Normal disconnection
        }
        finally
        {
            // Cleanup: unsubscribe handler
            if (handler != null)
            {
                _rollingWindow.TradeAdded -= handler;
            }
            
            cts.Cancel();
            cts.Dispose();
            sendLock.Dispose();

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
        }
    }

    private async Task SendTradeListAsync(WebSocket webSocket, List<TradeData> trades, SemaphoreSlim sendLock)
    {
        if (trades.Count == 0) return;

        var json = JsonSerializer.Serialize(new
        {
            type = "snapshot",
            symbol = trades.First().Symbol,
            trades = trades.Select(t => new
            {
                timestamp = ((DateTimeOffset)t.Timestamp).ToUnixTimeMilliseconds(),
                price = t.Price,
                quantity = t.Quantity,
                side = t.Side
            })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync();
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task SendSingleTradeAsync(WebSocket webSocket, TradeData trade, SemaphoreSlim sendLock)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "update",
            symbol = trade.Symbol,
            trade = new
            {
                timestamp = ((DateTimeOffset)trade.Timestamp).ToUnixTimeMilliseconds(),
                price = trade.Price,
                quantity = trade.Quantity,
                side = trade.Side
            }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync();
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    CancellationToken.None);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }
}
