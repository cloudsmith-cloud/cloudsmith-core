// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// Adds the Wave 1 canonical job dispatch columns to core.jobs per the frozen
/// contract (cloudsmith-internal design/api-surface/job-dispatch-contract.md §3, AB#4839):
/// idempotency_key (client dedupe, unique per org), site_id + env ((site_id, env)
/// relay routing scope), attempt_count (incremented on every → dispatched transition),
/// and timeout_at (absolute deadline for the API-side timeout watchdog).
/// </summary>
[Migration(20260703001)]
public sealed class M20260703001_AddJobDispatchColumns : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            ALTER TABLE core.jobs
                ADD COLUMN IF NOT EXISTS idempotency_key text,
                ADD COLUMN IF NOT EXISTS site_id         uuid REFERENCES core.sites (site_id) ON DELETE SET NULL,
                ADD COLUMN IF NOT EXISTS env             text,
                ADD COLUMN IF NOT EXISTS attempt_count   int  NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS timeout_at      timestamptz;

            -- Contract §4.1: create-side idempotency — a POST with an existing
            -- (org_id, idempotency_key) returns the existing job, never a new row.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_jobs_org_id_idempotency_key
                ON core.jobs (org_id, idempotency_key)
                WHERE idempotency_key IS NOT NULL;

            -- Contract §5: (site_id, env) routing — dispatch loop selects queued
            -- jobs by strict site/env equality with a connected relay.
            CREATE INDEX IF NOT EXISTS ix_jobs_site_id_env
                ON core.jobs (site_id, env)
                WHERE site_id IS NOT NULL;

            -- Contract §6.1: timeout watchdog sweep over non-terminal jobs.
            CREATE INDEX IF NOT EXISTS ix_jobs_timeout_at
                ON core.jobs (timeout_at)
                WHERE status NOT IN ('succeeded','failed','timed_out','cancelled');
            """);
    }

    public override void Down() { }
}
