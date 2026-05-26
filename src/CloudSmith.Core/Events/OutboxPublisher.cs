// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using CloudSmith.Sdk.Events;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CloudSmith.Core.Events;

/// <summary>
/// Persists events to the PostgreSQL-backed outbox table (core.event_outbox).
///
/// Callers write events via <see cref="EnqueueAsync"/> inside the same database
/// transaction as the business operation that raised the event (transactional outbox
/// pattern). A background <see cref="OutboxWorker"/> reads and delivers them.
///
/// AB#1425
/// </summary>
public sealed class OutboxPublisher
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(NpgsqlDataSource db, ILogger<OutboxPublisher> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Persist <paramref name="event"/> to the outbox so it is delivered at-least-once.
    /// </summary>
    public async Task EnqueueAsync<TEvent>(Guid orgId, TEvent @event, CancellationToken ct = default)
        where TEvent : ICloudSmithEvent
    {
        var eventType   = typeof(TEvent).FullName
            ?? typeof(TEvent).Name;
        var payloadJson = JsonSerializer.SerializeToDocument(@event, JsonOpts);

        const string sql = """
            INSERT INTO core.event_outbox (event_type, payload_json, org_id, next_attempt_at)
            VALUES (@event_type, @payload_json::jsonb, @org_id, now())
            """;

        await using var conn = await _db.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@event_type",   eventType);
        cmd.Parameters.AddWithValue("@payload_json", payloadJson.RootElement.GetRawText());
        cmd.Parameters.AddWithValue("@org_id",       orgId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        _logger.LogDebug("Outbox: enqueued {EventType} for org {OrgId}", eventType, orgId);
    }
}
