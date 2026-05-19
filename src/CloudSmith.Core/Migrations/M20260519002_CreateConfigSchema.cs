// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260519002, "Create config schema — 9 tables for Configuration Registry per ADR-042")]
public sealed class M20260519002_CreateConfigSchema : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS config;");

        // Recursive scope tree: org → environment → site → cluster → node → workload
        Execute.Sql("""
            CREATE TABLE config.scopes (
                scope_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                parent_scope_id uuid        REFERENCES config.scopes (scope_id) ON DELETE RESTRICT,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                scope_type      text        NOT NULL CHECK (scope_type IN ('org','environment','site','cluster','node','workload')),
                name            text        NOT NULL,
                created_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_scopes_org_parent_name UNIQUE (org_id, parent_scope_id, name)
            );
            CREATE INDEX ix_scopes_org_id ON config.scopes (org_id);
            CREATE INDEX ix_scopes_parent ON config.scopes (parent_scope_id) WHERE parent_scope_id IS NOT NULL;
            """);

        Execute.Sql("""
            CREATE TABLE config.variable_schema (
                variable_id     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                key             text        NOT NULL,
                description     text        NOT NULL DEFAULT '',
                group_name      text        NOT NULL DEFAULT 'general',
                scope_level     text        NOT NULL DEFAULT 'org',
                default_value   text,
                is_required     bool        NOT NULL DEFAULT false,
                is_secret       bool        NOT NULL DEFAULT false,
                lifecycle_state text        NOT NULL DEFAULT 'active' CHECK (lifecycle_state IN ('draft','active','deprecated','removed')),
                owning_module   text        NOT NULL DEFAULT 'core',
                created_at      timestamptz NOT NULL DEFAULT now(),
                updated_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_variable_schema_org_key UNIQUE (org_id, key)
            );
            CREATE INDEX ix_variable_schema_org_id ON config.variable_schema (org_id);
            CREATE INDEX ix_variable_schema_module ON config.variable_schema (owning_module);
            """);

        Execute.Sql("""
            CREATE TABLE config.variable_values (
                value_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                scope_id        uuid        NOT NULL REFERENCES config.scopes (scope_id) ON DELETE CASCADE,
                variable_id     uuid        NOT NULL REFERENCES config.variable_schema (variable_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                value           text,
                is_secret       bool        NOT NULL DEFAULT false,
                set_by          text        NOT NULL DEFAULT 'system',
                set_at          timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_variable_values_scope_var UNIQUE (scope_id, variable_id)
            );
            CREATE INDEX ix_variable_values_scope ON config.variable_values (scope_id);
            CREATE INDEX ix_variable_values_variable ON config.variable_values (variable_id);
            CREATE INDEX ix_variable_values_org ON config.variable_values (org_id);
            """);

        Execute.Sql("""
            CREATE TABLE config.variable_history (
                history_id      bigserial   PRIMARY KEY,
                value_id        uuid        NOT NULL REFERENCES config.variable_values (value_id) ON DELETE CASCADE,
                scope_id        uuid        NOT NULL,
                variable_id     uuid        NOT NULL,
                old_value       text,
                new_value       text,
                changed_by      text        NOT NULL,
                changed_at      timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_variable_history_value ON config.variable_history (value_id);
            CREATE INDEX ix_variable_history_changed_at ON config.variable_history (changed_at DESC);
            """);

        Execute.Sql("""
            CREATE TABLE config.resources (
                resource_id     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                scope_id        uuid        NOT NULL REFERENCES config.scopes (scope_id) ON DELETE RESTRICT,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                resource_type   text        NOT NULL,
                external_ids    jsonb       NOT NULL DEFAULT '{}',
                created_at      timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_resources_scope ON config.resources (scope_id);
            CREATE INDEX ix_resources_org ON config.resources (org_id);
            """);

        Execute.Sql("""
            CREATE TABLE config.deployment_snapshots (
                snapshot_id     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                resource_id     uuid        NOT NULL REFERENCES config.resources (resource_id) ON DELETE RESTRICT,
                scope_id        uuid        NOT NULL REFERENCES config.scopes (scope_id) ON DELETE RESTRICT,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                label           text        NOT NULL,
                snapshot_jsonb  jsonb       NOT NULL,
                captured_at     timestamptz NOT NULL DEFAULT now(),
                captured_by     text        NOT NULL
            );
            CREATE INDEX ix_deployment_snapshots_resource ON config.deployment_snapshots (resource_id);
            CREATE INDEX ix_deployment_snapshots_org ON config.deployment_snapshots (org_id, captured_at DESC);
            """);

        Execute.Sql("""
            CREATE TABLE config.drift_findings (
                finding_id      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                resource_id     uuid        NOT NULL REFERENCES config.resources (resource_id) ON DELETE CASCADE,
                snapshot_id     uuid        NOT NULL REFERENCES config.deployment_snapshots (snapshot_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                variable_key    text        NOT NULL,
                snapshot_value  text,
                current_value   text,
                detected_at     timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_drift_findings_resource ON config.drift_findings (resource_id);
            CREATE INDEX ix_drift_findings_org ON config.drift_findings (org_id, detected_at DESC);
            """);

        Execute.Sql("""
            CREATE TABLE config.stale_findings (
                finding_id      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                scope_id        uuid        NOT NULL REFERENCES config.scopes (scope_id) ON DELETE CASCADE,
                variable_id     uuid        NOT NULL REFERENCES config.variable_schema (variable_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                finding_type    text        NOT NULL CHECK (finding_type IN ('value_stale','schema_stale','definition_stale')),
                detected_at     timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_stale_findings_scope ON config.stale_findings (scope_id);
            CREATE INDEX ix_stale_findings_org ON config.stale_findings (org_id, detected_at DESC);
            """);

        Execute.Sql("""
            CREATE TABLE config.scope_group_activation (
                activation_id   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                scope_id        uuid        NOT NULL REFERENCES config.scopes (scope_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                group_name      text        NOT NULL,
                is_active       bool        NOT NULL DEFAULT true,
                updated_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_scope_group_activation UNIQUE (scope_id, group_name)
            );
            CREATE INDEX ix_scope_group_activation_scope ON config.scope_group_activation (scope_id);
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op
    }
}
