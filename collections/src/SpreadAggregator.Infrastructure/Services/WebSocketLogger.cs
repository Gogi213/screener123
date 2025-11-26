using System;

namespace SpreadAggregator.Infrastructure.Services;

/// <summary>
/// A simple static logger for WebSocket connection status.
/// REFACTORED: Removed file I/O, uses Console only for better performance.
/// </summary>
public static class WebSocketLogger
{
    /// <summary>
    /// Logs a message to the console.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message)
    {
        Console.WriteLine($"[WS] {DateTime.UtcNow:HH:mm:ss.fff} | {message}");
    }
}
