// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// Creates the PostgreSQL-backed event outbox table.
///
/// The outbox pattern ensures that domain events are durably persisted and
/// processed at-least-once even if the process crashes between event publication
/// and downstream delivery. A background worker polls the table, delivers pending
/// events in-process via IPlatformEventBus, and updates delivery status.
///
/// Columns:
///   event_id        — stable UUID per event instance
///   event_type      — CLR type name of the event (for deserialization)
///   payload_json    — serialized event payload (System.Text.Json)
///   org_id          — org scope for audit / observability
///   status          — pending | delivering | delivered | dead_letter
///   attempt_count   — number of delivery attempts
///   next_attempt_at — when the next delivery attempt is scheduled
///   last_error      — most recent delivery exception message
///   created_at      — immutable insertion time
///   delivered_at    — set when status transitions to delivered
///
/// AB#1425
/// </summary>
[Migration(20260526001, "Create event outbox table")]
public sealed class M20260526001_CreateEventOutbox : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE core.event_outbox (
                event_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                event_type      text        NOT NULL,
                payload_json    jsonb       NOT NULL,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE CASCADE,
                status          text        NOT NULL DEFAULT 'pending'
                                            CHECK (status IN ('pending', 'delivering', 'delivered', 'dead_letter')),
                attempt_count   int         NOT NULL DEFAULT 0,
                next_attempt_at timestamptz NOT NULL DEFAULT now(),
                last_error      text,
                created_at      timestamptz NOT NULL DEFAULT now(),
                delivered_at    timestamptz
            );

            -- Worker poll: find pending events due for delivery, ordered by creation time.
            CREATE INDEX ix_event_outbox_poll
                ON core.event_outbox (next_attempt_at, status)
                WHERE status IN ('pending', 'delivering');

            -- Org-scoped audit / observability.
            CREATE INDEX ix_event_outbox_org_created
                ON core.event_outbox (org_id, created_at DESC);

            -- Dead-letter monitoring.
            CREATE INDEX ix_event_outbox_dead_letter
                ON core.event_outbox (org_id, created_at DESC)
                WHERE status = 'dead_letter';
            """);
    }

    public override void Down()
    {
        // Forward-only — Down() is intentionally a no-op per CloudSmith migration policy.
    }
}
