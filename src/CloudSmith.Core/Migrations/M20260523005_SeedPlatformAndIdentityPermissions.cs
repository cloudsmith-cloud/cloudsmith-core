// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523005, "Seed platform/identity/audit/secrets permissions on PlatformAdmin role (Wave 1 + Wave 2 endpoints)")]
public sealed class M20260523005_SeedPlatformAndIdentityPermissions : Migration
{
    // Must match M20260519003_SeedBuiltInRoles
    private const string PlatformAdminRoleId = "00000000-0000-0000-0001-000000000001";

    public override void Up()
    {
        // Wave 1 + Wave 2 endpoints (Platform Management, Identity Providers,
        // Users/Invitations, Audit Log, Secret References) authorise via
        // PermissionRequirement strings. The first-run wizard creates a local
        // admin user mapped to the built-in PlatformAdmin role, so these
        // permissions must be attached to that role for the admin to actually
        // call the new endpoints.
        //
        // Schema note: there is no separate permission catalog table —
        // core.role_permissions stores (role_id, permission) text rows
        // directly (see M20260519001_CreateCoreSchema). audit:read was
        // already seeded onto PlatformAdmin in M20260519003 and is included
        // here for completeness with ON CONFLICT DO NOTHING.
        Execute.Sql($"""
            INSERT INTO core.role_permissions (role_id, permission)
            VALUES
                ('{PlatformAdminRoleId}', 'platform:read'),
                ('{PlatformAdminRoleId}', 'platform:write'),
                ('{PlatformAdminRoleId}', 'identity:read'),
                ('{PlatformAdminRoleId}', 'identity:write'),
                ('{PlatformAdminRoleId}', 'audit:read'),
                ('{PlatformAdminRoleId}', 'secrets:read'),
                ('{PlatformAdminRoleId}', 'secrets:write')
            ON CONFLICT (role_id, permission) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op
    }
}
