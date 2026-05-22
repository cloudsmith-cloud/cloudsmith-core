// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// ADR-047 — Bootstrap Authentication &amp; First-Run Setup.
/// Adds the platform setup-state singleton and the local (break-glass) credential
/// store so the platform can boot with no external IdP, land the operator in a
/// first-run setup wizard, and create a local administrator. The local admin
/// survives as a break-glass account after an IdP is later configured.
/// </summary>
[Migration(20260522001, "First-run setup state and local admin credentials (ADR-047)")]
public sealed class M20260522001_CreateSetupAndLocalAuth : Migration
{
    public override void Up()
    {
        // Singleton row tracking first-run completion. The `id` column is a fixed
        // boolean true with a CHECK + PK so only one row can ever exist.
        Execute.Sql("""
            CREATE TABLE core.platform_setup (
                id                   bool        PRIMARY KEY DEFAULT true,
                setup_state          text        NOT NULL DEFAULT 'NotStarted'
                                                 CHECK (setup_state IN ('NotStarted', 'Completed')),
                public_url           text,
                platform_name        text,
                timezone             text,
                completed_at         timestamptz,
                completed_by_user_id uuid        REFERENCES core.users (user_id) ON DELETE SET NULL,
                CONSTRAINT ck_platform_setup_singleton CHECK (id = true)
            );
            INSERT INTO core.platform_setup (id, setup_state) VALUES (true, 'NotStarted');
            """);

        // Local credentials for users that authenticate without an external IdP.
        // Password is stored as an Argon2id hash (never plaintext). The first local
        // admin is the permanent break-glass account (is_break_glass = true).
        Execute.Sql("""
            CREATE TABLE core.local_credentials (
                user_id        uuid        PRIMARY KEY REFERENCES core.users (user_id) ON DELETE CASCADE,
                username       text        NOT NULL,
                password_hash  text        NOT NULL,
                is_break_glass bool        NOT NULL DEFAULT false,
                created_at     timestamptz NOT NULL DEFAULT now(),
                updated_at     timestamptz NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX ux_local_credentials_username ON core.local_credentials (lower(username));
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
