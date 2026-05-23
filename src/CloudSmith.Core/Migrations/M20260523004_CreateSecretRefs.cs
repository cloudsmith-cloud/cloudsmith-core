// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523004, "Create core.secret_refs table (AB#1653)")]
public sealed class M20260523004_CreateSecretRefs : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE core.secret_refs (
                secret_ref_id   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                name            text        NOT NULL,
                ref_type        text        NOT NULL
                                            CHECK (ref_type IN ('api-key','client-secret','connection-string','certificate','arbitrary')),
                provider        text        NOT NULL
                                            CHECK (provider IN ('key-vault','local-encrypted','keycloak','manual')),
                vault_name      text,
                secret_path     text,
                version         text,
                last_rotated_at timestamptz,
                created_at      timestamptz NOT NULL DEFAULT now(),
                updated_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_secret_refs_org_name UNIQUE (org_id, name)
            );
            CREATE INDEX ix_secret_refs_org ON core.secret_refs (org_id);
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
