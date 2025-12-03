using SpreadAggregator.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// Defines a provider that supports real-time book ticker (best bid/ask) subscriptions.
/// </summary>
public interface IBookTickerProvider
{
    /// <summary>
    /// Subscribes to book ticker updates for a list of symbols.
    /// </summary>
    /// <param name="symbols">The symbols to subscribe to.</param>
    /// <param name="onData">The action to perform when new book ticker data arrives.</param>
    Task SubscribeToBookTickersAsync(IEnumerable<string> symbols, Func<BookTickerData, Task> onData);
}
