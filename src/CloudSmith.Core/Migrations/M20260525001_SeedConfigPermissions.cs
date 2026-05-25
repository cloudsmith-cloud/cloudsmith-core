// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260525001, "Seed config:read + config:write permissions for built-in roles")]
public sealed class M20260525001_SeedConfigPermissions : Migration
{
    private const string PlatformAdminRoleId = "00000000-0000-0000-0001-000000000001";
    private const string OrgAdminRoleId      = "00000000-0000-0000-0001-000000000002";
    private const string OrgViewerRoleId     = "00000000-0000-0000-0001-000000000003";

    public override void Up()
    {
        Execute.Sql($"""
            INSERT INTO core.role_permissions (role_id, permission)
            VALUES
                ('{PlatformAdminRoleId}', 'config:read'),
                ('{PlatformAdminRoleId}', 'config:write'),
                ('{OrgAdminRoleId}',      'config:read'),
                ('{OrgAdminRoleId}',      'config:write'),
                ('{OrgViewerRoleId}',     'config:read')
            ON CONFLICT (role_id, permission) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op
    }
}
