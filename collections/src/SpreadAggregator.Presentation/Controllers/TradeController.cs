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
    private readonly TradeAggregatorService _tradeAggregator;

    public TradeController(
        ILogger<TradeController> logger,
        TradeAggregatorService tradeAggregator)
    {
        _logger = logger;
        _tradeAggregator = tradeAggregator;
    }

    /// <summary>
    /// Get list of symbols with active trades (MEXC TRADES VIEWER API)
    /// </summary>
    [HttpGet("symbols")]
    public IActionResult GetSymbols()
    {
        var metadata = _tradeAggregator.GetAllSymbolsMetadata();

        var symbols = metadata
            .Select(m => new { m.Symbol, Exchange = "MEXC", m.LastPrice, m.LastUpdate })
            .OrderByDescending(x => x.LastUpdate)
            .ToList();

        return Ok(symbols);
    }

    // MEXC TRADES VIEWER: Legacy WebSocket endpoint disabled (use Fleck WebSocket on port 8181 instead)
    // /// <summary>
    // /// WebSocket endpoint for real-time trade streaming
    // /// </summary>
    // [HttpGet("/ws/trades/{symbol}")]
    // public async Task StreamTrades(string symbol)
    // {
    //     HttpContext.Response.StatusCode = StatusCodes.Status410Gone;
    //     await HttpContext.Response.WriteAsync("This endpoint is deprecated. Use ws://localhost:8181 instead.");
    // }
}
