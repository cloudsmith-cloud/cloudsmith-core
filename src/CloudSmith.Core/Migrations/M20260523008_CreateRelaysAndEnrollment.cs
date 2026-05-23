// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

[Migration(20260523008, "Create core.relays + core.relay_enrollment_tokens (AB#1670 — Relay bridge)")]
public sealed class M20260523008_CreateRelaysAndEnrollment : Migration
{
    public override void Up()
    {
        // core.relays — registry of all cloudsmith-relay agents enrolled to a PaaS org.
        // public_key_pem holds the Relay's identity certificate public key (MVP — full
        // cert issuance via cloudsmith-identity is a follow-up). status starts at
        // 'enrolled' on first call to /relays/enroll and is flipped to 'online' /
        // 'offline' by heartbeat updates and to 'revoked' by the DELETE endpoint.
        Execute.Sql("""
            CREATE TABLE core.relays (
                relay_id        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id          uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                site_id         uuid        REFERENCES core.sites (site_id) ON DELETE SET NULL,
                display_name    text        NOT NULL,
                public_key_pem  text        NOT NULL,
                enrolled_at     timestamptz NOT NULL DEFAULT now(),
                last_seen_at    timestamptz,
                status          text        NOT NULL DEFAULT 'enrolled'
                                            CHECK (status IN ('enrolled','online','offline','revoked')),
                CONSTRAINT uq_relays_org_displayname UNIQUE (org_id, display_name)
            );
            CREATE INDEX ix_relays_org    ON core.relays (org_id);
            CREATE INDEX ix_relays_status ON core.relays (org_id, status);
            """);

        // core.relay_enrollment_tokens — short-lived (default 1h) enrollment tokens.
        // Only the SHA-256 hash is stored; the plaintext token is returned exactly once
        // to the operator from POST /api/v1/relays/enroll-token and is never persisted.
        // The Relay presents the plaintext during enroll and the API hashes+compares.
        Execute.Sql("""
            CREATE TABLE core.relay_enrollment_tokens (
                token_id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id                uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                token_hash            text        NOT NULL UNIQUE,
                issued_by_user_id     uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                issued_at             timestamptz NOT NULL DEFAULT now(),
                expires_at            timestamptz NOT NULL,
                consumed_at           timestamptz,
                consumed_by_relay_id  uuid        REFERENCES core.relays (relay_id) ON DELETE SET NULL
            );
            CREATE INDEX ix_enroll_tokens_org_active
                ON core.relay_enrollment_tokens (org_id)
                WHERE consumed_at IS NULL;
            """);
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per CloudSmith migration policy
    }
}
