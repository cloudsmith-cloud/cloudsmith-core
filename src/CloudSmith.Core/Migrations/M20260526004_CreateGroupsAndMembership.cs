// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

/// <summary>
/// AB#1469 — Group CRUD and membership management.
///
/// core.groups — CloudSmith group definitions, optionally backed by an external
///   identity-provider group (e.g. an Active Directory security group synced via
///   Keycloak LDAP). An org-local group (external_group_id IS NULL) is managed
///   entirely within CloudSmith. A federated group mirrors AD/Keycloak membership.
///
/// core.group_members — User membership in groups. For federated groups, rows are
///   written by the Keycloak LDAP sync callback; for local groups, by the API.
///   The (group_id, user_id) pair is unique — a user may not be added twice.
///
/// core.group_role_mappings — Maps a group to a role so that RBAC resolves
///   permissions transitively through group membership (ADR-025).
/// </summary>
[Migration(20260526004)]
public sealed class M20260526004_CreateGroupsAndMembership : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS core.groups (
                group_id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                org_id              uuid        NOT NULL REFERENCES core.orgs (org_id) ON DELETE RESTRICT,
                name                text        NOT NULL,
                description         text,
                source              text        NOT NULL DEFAULT 'local'
                                                CHECK (source IN ('local','keycloak_ldap','entra_id')),
                external_group_id   text,       -- external group identifier (e.g. Keycloak group UUID, AD object SID)
                last_synced_at      timestamptz,
                created_at          timestamptz NOT NULL DEFAULT now(),
                updated_at          timestamptz NOT NULL DEFAULT now(),
                created_by_user_id  uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE RESTRICT,
                CONSTRAINT uq_groups_org_name UNIQUE (org_id, name)
            );

            CREATE INDEX IF NOT EXISTS ix_groups_org_id ON core.groups (org_id);
            CREATE INDEX IF NOT EXISTS ix_groups_org_source ON core.groups (org_id, source);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_groups_org_external ON core.groups (org_id, external_group_id)
                WHERE external_group_id IS NOT NULL;
            """);

        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS core.group_members (
                membership_id   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                group_id        uuid        NOT NULL REFERENCES core.groups (group_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL,
                user_id         uuid        NOT NULL REFERENCES core.users (user_id) ON DELETE CASCADE,
                added_at        timestamptz NOT NULL DEFAULT now(),
                added_by        text        NOT NULL DEFAULT 'admin',  -- 'admin' | 'ldap_sync' | 'entra_sync'
                CONSTRAINT uq_group_members_group_user UNIQUE (group_id, user_id)
            );

            CREATE INDEX IF NOT EXISTS ix_group_members_group_id ON core.group_members (group_id);
            CREATE INDEX IF NOT EXISTS ix_group_members_user_id ON core.group_members (user_id);
            CREATE INDEX IF NOT EXISTS ix_group_members_org_id ON core.group_members (org_id);
            """);

        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS core.group_role_mappings (
                mapping_id      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
                group_id        uuid        NOT NULL REFERENCES core.groups (group_id) ON DELETE CASCADE,
                org_id          uuid        NOT NULL,
                role_id         uuid        NOT NULL REFERENCES core.role_definitions (role_id) ON DELETE CASCADE,
                scope           text,
                created_at      timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_group_role_mappings_group_role_scope UNIQUE (group_id, role_id, scope)
            );

            CREATE INDEX IF NOT EXISTS ix_group_role_mappings_group_id ON core.group_role_mappings (group_id);
            CREATE INDEX IF NOT EXISTS ix_group_role_mappings_role_id ON core.group_role_mappings (role_id);
            """);
    }

    public override void Down() { }
}
