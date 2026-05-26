// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// Adds core.job_log for per-job structured log entries surfaced via GET /jobs/{id}/log.
/// The core.jobs table was created in M20260519001_CreateCoreSchema.
/// </summary>
[Migration(20260526002)]
public sealed class M20260526002_CreateJobLog : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS core.job_log (
                log_id      bigserial   PRIMARY KEY,
                job_id      uuid        NOT NULL REFERENCES core.jobs (job_id) ON DELETE CASCADE,
                org_id      uuid        NOT NULL,
                logged_at   timestamptz NOT NULL DEFAULT now(),
                severity    text        NOT NULL DEFAULT 'Info'
                                        CHECK (severity IN ('Info','Warning','Error')),
                message     text        NOT NULL,
                source      text
            );

            CREATE INDEX IF NOT EXISTS ix_job_log_job_id_logged_at ON core.job_log (job_id, logged_at);
            CREATE INDEX IF NOT EXISTS ix_job_log_org_id ON core.job_log (org_id);
            """);
    }

    public override void Down() { }
}
