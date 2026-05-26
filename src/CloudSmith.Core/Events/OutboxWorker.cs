// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using CloudSmith.Sdk.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CloudSmith.Core.Events;

/// <summary>
/// Background worker that delivers pending outbox events via <see cref="InProcessEventBus"/>.
///
/// Delivery semantics:
///   - Poll interval: 5 seconds.
///   - Retry schedule: 5s → 30s → 5m → 15m → 1h (exponential with cap), up to 5 attempts.
///   - Dead-letter: events that exceed 5 attempts are moved to status = 'dead_letter'.
///   - At-least-once: the worker marks an event 'delivering' before invoking the bus,
///     then 'delivered' on success.  A crash between those two points re-delivers on
///     the next poll (idempotent consumers recommended).
///
/// AB#1425
/// </summary>
public sealed class OutboxWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 5;

    // Retry delays indexed by attempt number (1-based).
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    private readonly NpgsqlDataSource _db;
    private readonly InProcessEventBus _bus;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        NpgsqlDataSource db,
        InProcessEventBus bus,
        ILogger<OutboxWorker> logger)
    {
        _db     = db;
        _bus    = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("OutboxWorker starting — poll interval {Interval}s", PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try { await DeliverPendingAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OutboxWorker: unhandled error in delivery poll");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task DeliverPendingAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);

        // Claim up to 50 pending events that are due.
        const string claimSql = """
            UPDATE core.event_outbox
            SET    status = 'delivering'
            WHERE  event_id IN (
                SELECT event_id FROM core.event_outbox
                WHERE  status IN ('pending', 'delivering')
                  AND  next_attempt_at <= now()
                ORDER BY created_at
                LIMIT  50
                FOR UPDATE SKIP LOCKED
            )
            RETURNING event_id, event_type, payload_json, attempt_count
            """;

        await using var claimCmd = new NpgsqlCommand(claimSql, conn);
        var pendingEvents = new List<(Guid EventId, string EventType, string Payload, int Attempts)>();

        await using (var reader = await claimCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                pendingEvents.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3)));
            }
        }

        if (pendingEvents.Count == 0) return;

        _logger.LogDebug("OutboxWorker: delivering {Count} pending events", pendingEvents.Count);

        foreach (var (eventId, eventType, payload, attempts) in pendingEvents)
        {
            if (ct.IsCancellationRequested) break;

            string? error = null;
            bool succeeded;

            try
            {
                // Publish as a generic OutboxEvent carrying the raw JSON for subscribers
                // that do not have access to the concrete event type.
                var envelope = new OutboxEventEnvelope(eventId, eventType, payload);
                await _bus.PublishAsync(envelope, ct).ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception ex)
            {
                error     = ex.Message;
                succeeded = false;
                _logger.LogWarning(ex,
                    "OutboxWorker: delivery failed for event {EventId} type={EventType} attempt={Attempt}",
                    eventId, eventType, attempts + 1);
            }

            if (succeeded)
            {
                const string doneSql = """
                    UPDATE core.event_outbox
                    SET status = 'delivered', delivered_at = now()
                    WHERE event_id = @id
                    """;
                await using var doneCmd = new NpgsqlCommand(doneSql, conn);
                doneCmd.Parameters.AddWithValue("@id", eventId);
                await doneCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var nextAttempt = attempts + 1;
                if (nextAttempt >= MaxAttempts)
                {
                    const string deadSql = """
                        UPDATE core.event_outbox
                        SET    status = 'dead_letter',
                               attempt_count = attempt_count + 1,
                               last_error = @error
                        WHERE  event_id = @id
                        """;
                    await using var deadCmd = new NpgsqlCommand(deadSql, conn);
                    deadCmd.Parameters.AddWithValue("@id",    eventId);
                    deadCmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
                    await deadCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                    _logger.LogError(
                        "OutboxWorker: event {EventId} dead-lettered after {MaxAttempts} attempts",
                        eventId, MaxAttempts);
                }
                else
                {
                    var delay = RetryDelays[Math.Min(nextAttempt - 1, RetryDelays.Length - 1)];
                    const string retrySql = """
                        UPDATE core.event_outbox
                        SET    status = 'pending',
                               attempt_count = attempt_count + 1,
                               last_error = @error,
                               next_attempt_at = now() + @delay
                        WHERE  event_id = @id
                        """;
                    await using var retryCmd = new NpgsqlCommand(retrySql, conn);
                    retryCmd.Parameters.AddWithValue("@id",    eventId);
                    retryCmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
                    retryCmd.Parameters.AddWithValue("@delay", delay);
                    await retryCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
        }
    }
}

/// <summary>
/// Generic envelope published via <see cref="InProcessEventBus"/> for all outbox events.
/// Subscribers that need the concrete type should deserialize <see cref="Payload"/> themselves.
/// OccurredAt reflects delivery time (not original event creation time).
/// SourceModuleId is "core.outbox" — the outbox worker is the re-publisher.
/// OrgId is null for the generic envelope; org context is in the Payload JSON if applicable.
/// </summary>
public sealed record OutboxEventEnvelope(
    Guid EventId,
    string EventType,
    string Payload) : ICloudSmithEvent
{
    public DateTimeOffset OccurredAt    { get; } = DateTimeOffset.UtcNow;
    public string         SourceModuleId { get; } = "core.outbox";
    public string?        OrgId          { get; } = null;
}
