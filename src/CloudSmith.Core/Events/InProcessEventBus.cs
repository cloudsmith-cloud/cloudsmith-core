// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace CloudSmith.Core.Events;

public sealed class InProcessEventBus : IPlatformEventBus
{
    private readonly Dictionary<Type, List<Func<ICloudSmithEvent, CancellationToken, Task>>> _handlers = new();
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(ILogger<InProcessEventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : ICloudSmithEvent
    {
        var key = typeof(TEvent);
        if (!_handlers.TryGetValue(key, out var list))
            _handlers[key] = list = new List<Func<ICloudSmithEvent, CancellationToken, Task>>();

        list.Add((e, ct) => handler((TEvent)e, ct));
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : ICloudSmithEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;

        foreach (var handler in handlers)
        {
            try
            {
                await handler(@event, cancellationToken);
            }
            catch (Exception ex)
            {
                // CS-CORE-WARN-001: subscriber exception swallowed; dispatch continues to remaining subscribers
                _logger.LogWarning(ex, "CS-CORE-WARN-001: event handler for {EventType} threw an exception", typeof(TEvent).Name);
            }
        }
    }
}
