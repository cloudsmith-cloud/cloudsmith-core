// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260519003, "Seed built-in platform roles — PlatformAdmin, OrgAdmin, OrgViewer, Auditor")]
public sealed class M20260519003_SeedBuiltInRoles : Migration
{
    // Fixed UUIDs for built-in roles — stable across all deployments
    private const string PlatformAdminRoleId = "00000000-0000-0000-0001-000000000001";
    private const string OrgAdminRoleId      = "00000000-0000-0000-0001-000000000002";
    private const string OrgViewerRoleId     = "00000000-0000-0000-0001-000000000003";
    private const string AuditorRoleId       = "00000000-0000-0000-0001-000000000004";

    public override void Up()
    {
        Execute.Sql($"""
            INSERT INTO core.role_definitions (role_id, org_id, name, description, is_built_in)
            VALUES
                ('{PlatformAdminRoleId}', NULL, 'PlatformAdmin', 'Full platform administration — all permissions', true),
                ('{OrgAdminRoleId}',      NULL, 'OrgAdmin',      'Organisation administrator — manage org resources', true),
                ('{OrgViewerRoleId}',     NULL, 'OrgViewer',     'Read-only access to org resources', true),
                ('{AuditorRoleId}',       NULL, 'Auditor',       'Read-only access to audit logs and org structure', true)
            ON CONFLICT (role_id) DO NOTHING;
            """);

        Execute.Sql($"""
            INSERT INTO core.role_permissions (role_id, permission)
            VALUES
                ('{PlatformAdminRoleId}', 'platform:admin'),
                ('{PlatformAdminRoleId}', 'org:admin'),
                ('{PlatformAdminRoleId}', 'cluster:write'),
                ('{PlatformAdminRoleId}', 'cluster:read'),
                ('{PlatformAdminRoleId}', 'hardware:write'),
                ('{PlatformAdminRoleId}', 'hardware:read'),
                ('{PlatformAdminRoleId}', 'config:write'),
                ('{PlatformAdminRoleId}', 'config:read'),
                ('{PlatformAdminRoleId}', 'audit:read'),
                ('{OrgAdminRoleId}',      'org:admin'),
                ('{OrgAdminRoleId}',      'cluster:write'),
                ('{OrgAdminRoleId}',      'cluster:read'),
                ('{OrgAdminRoleId}',      'hardware:read'),
                ('{OrgAdminRoleId}',      'config:write'),
                ('{OrgAdminRoleId}',      'config:read'),
                ('{OrgViewerRoleId}',     'org:read'),
                ('{OrgViewerRoleId}',     'cluster:read'),
                ('{OrgViewerRoleId}',     'hardware:read'),
                ('{OrgViewerRoleId}',     'config:read'),
                ('{AuditorRoleId}',       'audit:read'),
                ('{AuditorRoleId}',       'org:read')
            ON CONFLICT (role_id, permission) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op
    }
}
