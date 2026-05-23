// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523006, "Expand Wave 1/2 permissions to OrgAdmin, OrgViewer, and Auditor roles")]
public sealed class M20260523006_ExpandPermissionsToAllRoles : Migration
{
    // Must match M20260519003_SeedBuiltInRoles. PlatformAdmin already received
    // the full Wave 1/2 permission set in M20260523005 — this migration is
    // strictly additive for the other three built-in roles.
    private const string OrgAdminRoleId  = "00000000-0000-0000-0001-000000000002";
    private const string OrgViewerRoleId = "00000000-0000-0000-0001-000000000003";
    private const string AuditorRoleId   = "00000000-0000-0000-0001-000000000004";

    public override void Up()
    {
        // Rationale for each role's grants:
        //
        // OrgAdmin
        //   identity:read, identity:write — manages users IN their org
        //     (org-scoped; cross-org enforcement happens at the authorisation
        //      handler — the permission string is the same).
        //   platform:read — read-only view of platform health/sites/secrets
        //     so an org admin can see the platform context their org runs in
        //     without granting write.
        //   audit:read — org-scoped audit visibility.
        //
        // OrgViewer
        //   identity:read, platform:read, audit:read — fully read-only
        //     observer of org users, platform context, and audit trail.
        //
        // Auditor
        //   audit:read — and only that. Auditor is the dedicated read-only
        //     audit role. It is already seeded in M20260519003, so this is
        //     guaranteed to apply. The INSERT below is still emitted for
        //     PlatformAdmin/OrgAdmin/OrgViewer/Auditor with
        //     ON CONFLICT DO NOTHING so re-runs and any future role-set
        //     adjustments remain safe and idempotent.
        Execute.Sql($"""
            INSERT INTO core.role_permissions (role_id, permission)
            VALUES
                ('{OrgAdminRoleId}',  'identity:read'),
                ('{OrgAdminRoleId}',  'identity:write'),
                ('{OrgAdminRoleId}',  'platform:read'),
                ('{OrgAdminRoleId}',  'audit:read'),
                ('{OrgViewerRoleId}', 'identity:read'),
                ('{OrgViewerRoleId}', 'platform:read'),
                ('{OrgViewerRoleId}', 'audit:read'),
                ('{AuditorRoleId}',   'audit:read')
            ON CONFLICT (role_id, permission) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Forward-only — no-op per CloudSmith migration policy
    }
}
