// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using CloudSmith.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace CloudSmith.Core.Events;

public sealed class InProcessEventBus : IPlatformEventBus
{
    private sealed class Subscription
    {
        public required Func<ICloudSmithEvent, bool> Filter { get; init; }
        public required Func<ICloudSmithEvent, CancellationToken, Task> Handler { get; init; }
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;
        public Unsubscriber(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose();
        }
    }

    private readonly Dictionary<Type, List<Subscription>> _handlers = new();
    private readonly object _lock = new();
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(ILogger<InProcessEventBus> logger)
    {
        _logger = logger;
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : ICloudSmithEvent
        => SubscribeWithFilter<TEvent>(_ => true, handler);

    public IDisposable SubscribeWithFilter<TEvent>(
        Func<TEvent, bool> filter,
        Func<TEvent, CancellationToken, Task> handler)
        where TEvent : ICloudSmithEvent
    {
        var key = typeof(TEvent);
        var sub = new Subscription
        {
            Filter = e =>
            {
                try { return filter((TEvent)e); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CS-SDK-5003: event filter for {EventType} threw; treated as non-match", key.Name);
                    return false;
                }
            },
            Handler = (e, ct) => handler((TEvent)e, ct)
        };

        lock (_lock)
        {
            if (!_handlers.TryGetValue(key, out var list))
                _handlers[key] = list = new List<Subscription>();
            list.Add(sub);
        }

        return new Unsubscriber(() =>
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(key, out var list))
                    list.Remove(sub);
            }
        });
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : ICloudSmithEvent
    {
        List<Subscription> snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list)) return;
            snapshot = new List<Subscription>(list);
        }

        foreach (var sub in snapshot)
        {
            try
            {
                if (!sub.Filter(@event)) continue;
                await sub.Handler(@event, ct);
            }
            catch (Exception ex)
            {
                // CS-SDK-5001: subscriber exception swallowed; dispatch continues to remaining subscribers
                _logger.LogWarning(ex, "CS-SDK-5001: event handler for {EventType} threw an exception", typeof(TEvent).Name);
            }
        }
    }
}
