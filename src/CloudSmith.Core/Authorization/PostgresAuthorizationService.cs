// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Sdk.Authorization;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CloudSmith.Core.Authorization;

public sealed class PostgresAuthorizationService : ICloudSmithAuthorizationService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<PostgresAuthorizationService> _logger;

    public PostgresAuthorizationService(NpgsqlDataSource db, ILogger<PostgresAuthorizationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasPermissionAsync(AuthorizationContext context, string permission, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        // Set org_id session variable for RLS enforcement
        await using (var setOrg = conn.CreateCommand())
        {
            setOrg.CommandText = "SET LOCAL app.current_org_id = @orgId;";
            setOrg.Parameters.AddWithValue("orgId", context.OrgId);
            await setOrg.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM core.role_assignments ra
                JOIN core.role_permissions rp ON rp.role_id = ra.role_id
                WHERE ra.org_id    = @orgId
                  AND ra.user_id   = @userId
                  AND rp.permission IN (@permission, 'platform:admin')
                  AND (ra.expires_at IS NULL OR ra.expires_at > now())
                  AND (ra.scope IS NULL
                       OR ra.scope = @siteScope
                       OR ra.scope = @clusterScope)
            )
            """;
        cmd.Parameters.AddWithValue("orgId",       context.OrgId);
        cmd.Parameters.AddWithValue("userId",      context.UserId);
        cmd.Parameters.AddWithValue("permission",  permission);
        cmd.Parameters.AddWithValue("siteScope",   context.SiteId.HasValue ? $"site:{context.SiteId}" : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("clusterScope", context.ClusterId.HasValue ? $"cluster:{context.ClusterId}" : (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(AuthorizationContext context, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT rp.permission
            FROM core.role_assignments ra
            JOIN core.role_permissions rp ON rp.role_id = ra.role_id
            WHERE ra.org_id  = @orgId
              AND ra.user_id = @userId
              AND (ra.expires_at IS NULL OR ra.expires_at > now())
            ORDER BY rp.permission
            """;
        cmd.Parameters.AddWithValue("orgId",  context.OrgId);
        cmd.Parameters.AddWithValue("userId", context.UserId);

        var permissions = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            permissions.Add(reader.GetString(0));

        return permissions;
    }
}
