// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523001, "Create core.identity_providers table (AB#1643)")]
public sealed class M20260523001_CreateIdentityProviders : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE core.identity_providers (
                idp_id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                idp_type            text        NOT NULL
                                                CHECK (idp_type IN ('entra-id','active-directory','oidc','keycloak')),
                display_name        text        NOT NULL,
                authority           text,
                client_id           text,
                client_secret_ref   text,
                status              text        NOT NULL DEFAULT 'configured'
                                                CHECK (status IN ('configured','verified','disabled','error')),
                config_json         jsonb       NOT NULL DEFAULT '{}'::jsonb,
                configured_at       timestamptz NOT NULL DEFAULT now(),
                updated_at          timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_idp_org_displayname UNIQUE (org_id, display_name)
            );
            CREATE INDEX ix_idp_org ON core.identity_providers (org_id);
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
