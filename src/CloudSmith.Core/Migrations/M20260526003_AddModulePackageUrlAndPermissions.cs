// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// AB#1414 — manifest validation requires package_url persisted in module_registry so
///           that re-validation and re-download are possible without re-submitting the URL.
/// AB#1417 — module permissions seeded from manifest on install; core.module_permissions
///           tracks which permissions a module has registered so they can be cleaned up
///           on uninstall and queried for RBAC grant flows.
/// </summary>
[Migration(20260526003)]
public sealed class M20260526003_AddModulePackageUrlAndPermissions : Migration
{
    public override void Up()
    {
        // Add package_url to module_registry — was missing from the original schema
        // but referenced in the install endpoint. Use ALTER TABLE … ADD COLUMN IF NOT EXISTS
        // for idempotency in case this migration runs more than once on a recovered instance.
        Execute.Sql("""
            ALTER TABLE core.module_registry
                ADD COLUMN IF NOT EXISTS package_url text NOT NULL DEFAULT '';
            """);

        // module_permissions — permissions declared by a module manifest, seeded on install,
        // removed on uninstall. A permission may be registered by multiple modules (rare but valid).
        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS core.module_permissions (
                module_permission_id    uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id                  uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                module_id               uuid        NOT NULL REFERENCES core.module_registry (module_id) ON DELETE CASCADE,
                permission              text        NOT NULL,
                description             text,
                created_at              timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_module_permissions_org_module_perm UNIQUE (org_id, module_id, permission)
            );

            CREATE INDEX IF NOT EXISTS ix_module_permissions_org_module ON core.module_permissions (org_id, module_id);
            CREATE INDEX IF NOT EXISTS ix_module_permissions_perm ON core.module_permissions (org_id, permission);
            """);
    }

    public override void Down() { }
}
