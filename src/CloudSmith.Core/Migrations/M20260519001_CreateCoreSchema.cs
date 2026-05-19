// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260519001, "Create core schema and all platform tables")]
public sealed class M20260519001_CreateCoreSchema : Migration
{
    public override void Up()
    {
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS core;");

        Execute.Sql("""
            CREATE TABLE core.orgs (
                org_id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                name            text        NOT NULL,
                slug            text        NOT NULL UNIQUE,
                tier            text        NOT NULL DEFAULT 'standard'
                                            CHECK (tier IN ('standard', 'msp_managed', 'msp_root')),
                msp_org_id      uuid        REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                oidc_issuer     text,
                oidc_client_id  text,
                created_at      timestamptz NOT NULL DEFAULT now(),
                updated_at      timestamptz NOT NULL DEFAULT now(),
                deleted_at      timestamptz
            );
            CREATE INDEX ix_orgs_msp_org_id ON core.orgs (msp_org_id) WHERE msp_org_id IS NOT NULL;
            CREATE INDEX ix_orgs_slug ON core.orgs (slug) WHERE deleted_at IS NULL;
            """);

        Execute.Sql("""
            CREATE TABLE core.users (
                user_id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                external_id     text        NOT NULL,
                email           text        NOT NULL,
                display_name    text        NOT NULL,
                is_active       bool        NOT NULL DEFAULT true,
                last_login_at   timestamptz,
                created_at      timestamptz NOT NULL DEFAULT now(),
                updated_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_users_org_external UNIQUE (org_id, external_id)
            );
            CREATE INDEX ix_users_org_id ON core.users (org_id) WHERE is_active = true;
            CREATE INDEX ix_users_org_email ON core.users (org_id, email) WHERE is_active = true;
            """);

        Execute.Sql("""
            CREATE TABLE core.role_definitions (
                role_id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                name            text        NOT NULL,
                description     text        NOT NULL DEFAULT '',
                is_built_in     bool        NOT NULL DEFAULT false,
                created_at      timestamptz NOT NULL DEFAULT now(),
                updated_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_role_definitions_org_name UNIQUE (org_id, name)
            );
            CREATE INDEX ix_role_definitions_org_id ON core.role_definitions (org_id) WHERE org_id IS NOT NULL;
            CREATE INDEX ix_role_definitions_built_in ON core.role_definitions (is_built_in) WHERE is_built_in = true;
            """);

        Execute.Sql("""
            CREATE TABLE core.role_permissions (
                role_id     uuid    NOT NULL REFERENCES core.role_definitions (role_id) ON DELETE CASCADE,
                permission  text    NOT NULL,
                PRIMARY KEY (role_id, permission)
            );
            CREATE INDEX ix_role_permissions_permission ON core.role_permissions (permission);
            """);

        Execute.Sql("""
            CREATE TABLE core.role_assignments (
                assignment_id       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                user_id             uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE CASCADE,
                role_id             uuid        NOT NULL REFERENCES core.role_definitions (role_id) ON DELETE RESTRICT,
                scope               text,
                assigned_by_user_id uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                created_at          timestamptz NOT NULL DEFAULT now(),
                expires_at          timestamptz,
                CONSTRAINT uq_role_assignments_user_role_scope UNIQUE (user_id, role_id, scope)
            );
            CREATE INDEX ix_role_assignments_org_user ON core.role_assignments (org_id, user_id);
            CREATE INDEX ix_role_assignments_expires ON core.role_assignments (expires_at) WHERE expires_at IS NOT NULL;
            """);

        Execute.Sql("""
            CREATE TABLE core.module_registry (
                module_id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                package_id          text        NOT NULL,
                display_name        text        NOT NULL,
                version             text        NOT NULL,
                sdk_version         text        NOT NULL,
                status              text        NOT NULL DEFAULT 'installing'
                                                CHECK (status IN ('installing','enabled','degraded','disabled','uninstalling','error')),
                manifest_json       jsonb       NOT NULL,
                installed_at        timestamptz NOT NULL DEFAULT now(),
                updated_at          timestamptz NOT NULL DEFAULT now(),
                installed_by_user_id uuid       NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                CONSTRAINT uq_module_registry_org_package UNIQUE (org_id, package_id)
            );
            CREATE INDEX ix_module_registry_org_id ON core.module_registry (org_id);
            CREATE INDEX ix_module_registry_status ON core.module_registry (org_id, status);
            """);

        Execute.Sql("""
            CREATE TABLE core.sites (
                site_id     uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id      uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                name        text        NOT NULL,
                description text,
                location    text,
                created_at  timestamptz NOT NULL DEFAULT now(),
                updated_at  timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_sites_org_name UNIQUE (org_id, name)
            );
            CREATE INDEX ix_sites_org_id ON core.sites (org_id);
            """);

        Execute.Sql("""
            CREATE TABLE core.runner_registry (
                runner_id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                site_id             uuid        REFERENCES core.sites (site_id) ON DELETE SET NULL,
                name                text        NOT NULL,
                hostname            text        NOT NULL,
                version             text        NOT NULL,
                os_platform         text        NOT NULL CHECK (os_platform IN ('windows','linux')),
                cert_thumbprint     text        NOT NULL,
                status              text        NOT NULL DEFAULT 'offline' CHECK (status IN ('online','offline','degraded')),
                last_heartbeat_at   timestamptz,
                capabilities        text[]      NOT NULL DEFAULT '{}',
                registered_at       timestamptz NOT NULL DEFAULT now(),
                updated_at          timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_runner_registry_org_hostname UNIQUE (org_id, hostname)
            );
            CREATE INDEX ix_runner_registry_org_id ON core.runner_registry (org_id);
            CREATE INDEX ix_runner_registry_org_status ON core.runner_registry (org_id, status);
            CREATE INDEX ix_runner_registry_site_id ON core.runner_registry (site_id) WHERE site_id IS NOT NULL;
            """);

        Execute.Sql("""
            CREATE TABLE core.jobs (
                job_id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                runner_id           uuid        REFERENCES core.runner_registry (runner_id) ON DELETE SET NULL,
                module_id           uuid        REFERENCES core.module_registry (module_id) ON DELETE SET NULL,
                job_type            text        NOT NULL,
                status              text        NOT NULL DEFAULT 'queued'
                                                CHECK (status IN ('queued','dispatched','running','succeeded','failed','cancelled','timed_out')),
                payload_json        jsonb       NOT NULL DEFAULT '{}',
                result_json         jsonb,
                error_code          text,
                error_message       text,
                started_at          timestamptz,
                completed_at        timestamptz,
                created_by_user_id  uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                created_at          timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_jobs_org_id_status ON core.jobs (org_id, status);
            CREATE INDEX ix_jobs_org_id_created_at ON core.jobs (org_id, created_at DESC);
            CREATE INDEX ix_jobs_runner_id ON core.jobs (runner_id) WHERE runner_id IS NOT NULL;
            CREATE INDEX ix_jobs_module_id ON core.jobs (module_id) WHERE module_id IS NOT NULL;
            """);

        // audit_log intentionally has no FK on org_id — orphan audit rows must be preserved when orgs are soft-deleted
        Execute.Sql("""
            CREATE TABLE core.audit_log (
                audit_id        bigserial   PRIMARY KEY,
                org_id          uuid        NOT NULL,
                user_id         uuid,
                action          text        NOT NULL,
                resource_type   text        NOT NULL,
                resource_id     uuid,
                before_json     jsonb,
                after_json      jsonb,
                ip_address      text,
                user_agent      text,
                correlation_id  uuid,
                occurred_at     timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_audit_log_org_occurred ON core.audit_log (org_id, occurred_at DESC);
            CREATE INDEX ix_audit_log_resource ON core.audit_log (org_id, resource_type, resource_id) WHERE resource_id IS NOT NULL;
            CREATE INDEX ix_audit_log_user ON core.audit_log (org_id, user_id) WHERE user_id IS NOT NULL;
            CREATE INDEX ix_audit_log_correlation ON core.audit_log (correlation_id) WHERE correlation_id IS NOT NULL;
            """);

        Execute.Sql("""
            CREATE TABLE core.notifications (
                notification_id uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                user_id         uuid        REFERENCES core.users (user_id) ON DELETE CASCADE,
                type            text        NOT NULL CHECK (type IN ('alert','job_complete','module_event','system')),
                title           text        NOT NULL,
                body            text        NOT NULL,
                is_read         bool        NOT NULL DEFAULT false,
                created_at      timestamptz NOT NULL DEFAULT now(),
                read_at         timestamptz
            );
            CREATE INDEX ix_notifications_org_user ON core.notifications (org_id, user_id)
                WHERE user_id IS NOT NULL AND is_read = false;
            CREATE INDEX ix_notifications_org_broadcast ON core.notifications (org_id, created_at DESC)
                WHERE user_id IS NULL;
            """);

        Execute.Sql("""
            CREATE TABLE core.secrets_references (
                ref_id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                name                text        NOT NULL,
                description         text,
                provider            text        NOT NULL CHECK (provider IN ('key_vault','postgres_encrypted','openbao','environment')),
                external_key        text        NOT NULL,
                created_by_user_id  uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                created_at          timestamptz NOT NULL DEFAULT now(),
                last_rotated_at     timestamptz,
                CONSTRAINT uq_secrets_references_org_name UNIQUE (org_id, name)
            );
            CREATE INDEX ix_secrets_references_org_id ON core.secrets_references (org_id);
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
