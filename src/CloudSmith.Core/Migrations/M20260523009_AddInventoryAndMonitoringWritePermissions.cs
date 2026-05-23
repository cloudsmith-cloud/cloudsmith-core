// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523009, "Seed inventory:write + monitoring:write permissions for Relay bridge endpoints (AB#1670)")]
public sealed class M20260523009_AddInventoryAndMonitoringWritePermissions : Migration
{
    // Must match M20260519003_SeedBuiltInRoles.
    private const string PlatformAdminRoleId = "00000000-0000-0000-0001-000000000001";
    private const string OrgAdminRoleId      = "00000000-0000-0000-0001-000000000002";

    public override void Up()
    {
        // The Relay bridge endpoints (POST /api/v1/inventory/ingest and
        // POST /api/v1/health/probe-result) authorise via PermissionRequirement
        // strings — they need inventory:write and monitoring:write respectively.
        //
        // Grant both to PlatformAdmin (the first-run wizard's bootstrap role)
        // and OrgAdmin (the role used by operator-issued enrollment tokens,
        // which the Relay then echoes via its JWT). Other built-in roles
        // (OrgViewer, Auditor) stay read-only and do not receive these.
        Execute.Sql($"""
            INSERT INTO core.role_permissions (role_id, permission)
            VALUES
                ('{PlatformAdminRoleId}', 'inventory:write'),
                ('{PlatformAdminRoleId}', 'monitoring:write'),
                ('{OrgAdminRoleId}',      'inventory:write'),
                ('{OrgAdminRoleId}',      'monitoring:write')
            ON CONFLICT (role_id, permission) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op
    }
}
