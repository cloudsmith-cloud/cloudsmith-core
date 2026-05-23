// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using FluentMigrator;

namespace CloudSmith.Core.Migrations;

// ----------------------------------------------------------------------------
// Reconciliation: core.secrets_references  ->  core.secret_refs
//
// Two competing tables existed for the "secret reference" concept after
// AB#1653 landed:
//
//   1. core.secrets_references — original, defined in
//      M20260519001_CreateCoreSchema.cs. Columns:
//          ref_id, org_id, name, description, provider
//          (key_vault|postgres_encrypted|openbao|environment),
//          external_key, created_by_user_id, created_at, last_rotated_at.
//      No service code reads or writes this table.
//
//   2. core.secret_refs — added 2026-05-23 by the AB#1653 agent in
//      M20260523004_CreateSecretRefs.cs. Columns:
//          secret_ref_id, org_id, name, ref_type, provider
//          (key-vault|local-encrypted|keycloak|manual), vault_name,
//          secret_path, version, last_rotated_at, created_at, updated_at.
//      This is the schema the Wave 2 Secret References endpoint and the
//      portal client expect.
//
// Canonical choice: keep core.secret_refs. Drop core.secrets_references.
//
// Grep results that informed the decision (cloudsmith-core,
// cloudsmith-secrets, cloudsmith-api on 2026-05-23):
//
//   cloudsmith-core/src/CloudSmith.Core/Migrations/
//     M20260519001_CreateCoreSchema.cs        — the table's own DDL.
//   cloudsmith-core/src/CloudSmith.Core.Tests/Migrations/
//     MigrationTests.cs                       — asserts the table exists in
//                                               the "all tables present"
//                                               schema test. Updated in
//                                               the same change as this
//                                               migration to assert
//                                               core.secret_refs instead.
//   cloudsmith-secrets/src/CloudSmith.Secrets/SecretReference.cs
//                                             — doc comment only; the class
//                                               parses backend URIs and does
//                                               NOT query the table.
//   cloudsmith-api                            — no references.
//
// Conclusion: no production code consumes core.secrets_references, so no
// data migration is required. The table is dropped outright.
// ----------------------------------------------------------------------------

[Migration(20260523007, "Drop redundant core.secrets_references; core.secret_refs is canonical")]
public sealed class M20260523007_ReconcileSecretsReferencesTables : Migration
{
    public override void Up()
    {
        // CASCADE is defensive — the table currently has no inbound foreign
        // keys, but the indexes ix_secrets_references_org_id and the
        // uq_secrets_references_org_name constraint are dropped along with
        // it. No data migration is performed because no service consumes
        // the table (see file header for the grep audit).
        Execute.Sql("DROP TABLE IF EXISTS core.secrets_references CASCADE;");
    }

    public override void Down()
    {
        // Forward-only migrations — Down() is intentionally a no-op per
        // CloudSmith migration policy.
    }
}
