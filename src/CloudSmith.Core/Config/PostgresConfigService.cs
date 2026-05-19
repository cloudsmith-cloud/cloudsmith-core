// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Sdk.Config;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CloudSmith.Core.Config;

public sealed class PostgresConfigService : ICloudSmithConfigService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresConfigService> _logger;

    public PostgresConfigService(NpgsqlDataSource db, ILogger<PostgresConfigService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ConfigValue?> GetValueAsync(string scopeId, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT vv.value, vv.set_by, vv.set_at, vv.is_secret
            FROM config.variable_values vv
            JOIN config.variable_schema vs ON vs.variable_id = vv.variable_id
            JOIN config.scopes s ON s.scope_id = vv.scope_id
            WHERE s.scope_id = @scopeId::uuid AND vs.key = @key
            """;
        cmd.Parameters.AddWithValue("scopeId", scopeId);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new ConfigValue(
            ScopeId: scopeId,
            Key: key,
            Value: reader.GetString(0),
            SetBy: reader.GetString(1),
            SetAt: reader.GetFieldValue<DateTimeOffset>(2),
            IsSecret: reader.GetBoolean(3),
            ProvidedByScopeId: scopeId);
    }

    public async Task<ConfigValue?> GetEffectiveValueAsync(string scopeId, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // Recursive CTE walks the scope ancestor chain; closest ancestor wins
        cmd.CommandText = """
            WITH RECURSIVE scope_chain AS (
                SELECT scope_id, parent_scope_id, 0 AS depth
                FROM config.scopes
                WHERE scope_id = @scopeId::uuid

                UNION ALL

                SELECT s.scope_id, s.parent_scope_id, sc.depth + 1
                FROM config.scopes s
                JOIN scope_chain sc ON sc.parent_scope_id = s.scope_id
            )
            SELECT vv.value, vv.set_by, vv.set_at, vv.is_secret, sc.scope_id::text AS provided_by
            FROM scope_chain sc
            JOIN config.variable_values vv ON vv.scope_id = sc.scope_id
            JOIN config.variable_schema vs ON vs.variable_id = vv.variable_id AND vs.key = @key
            ORDER BY sc.depth ASC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("scopeId", scopeId);
        cmd.Parameters.AddWithValue("key", key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Fall back to schema default_value if defined
            return await GetSchemaDefaultAsync(conn, scopeId, key, cancellationToken);
        }

        return new ConfigValue(
            ScopeId: scopeId,
            Key: key,
            Value: reader.GetString(0),
            SetBy: reader.GetString(1),
            SetAt: reader.GetFieldValue<DateTimeOffset>(2),
            IsSecret: reader.GetBoolean(3),
            ProvidedByScopeId: reader.GetString(4));
    }

    private static async Task<ConfigValue?> GetSchemaDefaultAsync(NpgsqlConnection conn, string scopeId, string key, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT default_value, is_secret FROM config.variable_schema WHERE key = @key AND default_value IS NOT NULL LIMIT 1";
        cmd.Parameters.AddWithValue("key", key);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ConfigValue(scopeId, key, reader.GetString(0), "schema-default", DateTimeOffset.MinValue, reader.GetBoolean(1), "schema");
    }

    public async Task SetValueAsync(string scopeId, string key, string value, bool isSecret = false, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO config.variable_values (scope_id, variable_id, org_id, value, is_secret, set_by, set_at)
            SELECT @scopeId::uuid, vs.variable_id,
                   (SELECT org_id FROM config.scopes WHERE scope_id = @scopeId::uuid),
                   @value, @isSecret, current_user, now()
            FROM config.variable_schema vs WHERE vs.key = @key
            ON CONFLICT (scope_id, variable_id) DO UPDATE
                SET value = EXCLUDED.value, is_secret = EXCLUDED.is_secret,
                    set_by = EXCLUDED.set_by, set_at = EXCLUDED.set_at
            """;
        cmd.Parameters.AddWithValue("scopeId", scopeId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("isSecret", isSecret);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteValueAsync(string scopeId, string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM config.variable_values vv
            USING config.variable_schema vs
            WHERE vv.variable_id = vs.variable_id
              AND vs.key = @key
              AND vv.scope_id = @scopeId::uuid
            """;
        cmd.Parameters.AddWithValue("scopeId", scopeId);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConfigVariable>> ListVariablesAsync(string? owningModule = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT key, description, group_name, default_value, is_required, is_secret, owning_module
            FROM config.variable_schema
            WHERE lifecycle_state = 'active'
              AND (@module IS NULL OR owning_module = @module)
            ORDER BY owning_module, key
            """;
        cmd.Parameters.AddWithValue("module", (object?)owningModule ?? DBNull.Value);

        var results = new List<ConfigVariable>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ConfigVariable(
                Key: reader.GetString(0),
                Description: reader.GetString(1),
                Group: reader.GetString(2),
                DefaultValue: reader.IsDBNull(3) ? null : reader.GetString(3),
                IsRequired: reader.GetBoolean(4),
                IsSecret: reader.GetBoolean(5),
                OwningModule: reader.GetString(6)));
        }
        return results;
    }

    public async Task<ConfigSnapshot> GetSnapshotAsync(string scopeId, string label, string? snapshotId = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        if (snapshotId is not null)
        {
            await using var fetchCmd = conn.CreateCommand();
            fetchCmd.CommandText = "SELECT snapshot_id, scope_id, label, captured_at, snapshot_jsonb FROM config.deployment_snapshots WHERE snapshot_id = @id::uuid LIMIT 1";
            fetchCmd.Parameters.AddWithValue("id", snapshotId);
            await using var r = await fetchCmd.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                return new ConfigSnapshot(
                    r.GetString(0), r.GetString(1), r.GetString(2),
                    r.GetFieldValue<DateTimeOffset>(3),
                    new Dictionary<string, ConfigValue>());
            }
        }

        // Capture current effective values for all known variables at this scope
        var variables = await CollectEffectiveValuesAsync(conn, scopeId, cancellationToken);
        var newId = Guid.NewGuid().ToString();

        // Snapshot is written to deployment_snapshots when a resource_id is available;
        // for API-triggered snapshots without a resource_id, return an in-memory snapshot
        return new ConfigSnapshot(newId, scopeId, label, DateTimeOffset.UtcNow, variables);
    }

    private async Task<IReadOnlyDictionary<string, ConfigValue>> CollectEffectiveValuesAsync(
        NpgsqlConnection conn, string scopeId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH RECURSIVE scope_chain AS (
                SELECT scope_id, parent_scope_id, 0 AS depth FROM config.scopes WHERE scope_id = @scopeId::uuid
                UNION ALL
                SELECT s.scope_id, s.parent_scope_id, sc.depth + 1
                FROM config.scopes s JOIN scope_chain sc ON sc.parent_scope_id = s.scope_id
            ),
            effective AS (
                SELECT DISTINCT ON (vs.key)
                    vs.key, vv.value, vv.set_by, vv.set_at, vv.is_secret, sc.scope_id::text AS provided_by
                FROM scope_chain sc
                JOIN config.variable_values vv ON vv.scope_id = sc.scope_id
                JOIN config.variable_schema vs ON vs.variable_id = vv.variable_id
                ORDER BY vs.key, sc.depth ASC
            )
            SELECT key, value, set_by, set_at, is_secret, provided_by FROM effective
            """;
        cmd.Parameters.AddWithValue("scopeId", scopeId);

        var dict = new Dictionary<string, ConfigValue>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var k = reader.GetString(0);
            dict[k] = new ConfigValue(scopeId, k, reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3), reader.GetBoolean(4), reader.GetString(5));
        }
        return dict;
    }
}
