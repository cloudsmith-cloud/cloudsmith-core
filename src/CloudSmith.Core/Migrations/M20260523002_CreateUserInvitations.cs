// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523002, "Create core.user_invitations table (AB#1649)")]
public sealed class M20260523002_CreateUserInvitations : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE core.user_invitations (
                invitation_id       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                email               text        NOT NULL,
                invited_by_user_id  uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                roles               jsonb       NOT NULL DEFAULT '[]'::jsonb,
                token               text        NOT NULL UNIQUE,
                status              text        NOT NULL DEFAULT 'pending'
                                                CHECK (status IN ('pending','accepted','revoked','expired')),
                expires_at          timestamptz NOT NULL,
                created_at          timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_invitations_org ON core.user_invitations (org_id);
            CREATE INDEX ix_invitations_status ON core.user_invitations (org_id, status);
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
