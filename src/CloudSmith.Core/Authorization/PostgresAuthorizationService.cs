// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Sdk.Authorization;
using CloudSmith.Sdk.Permissions;
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

    public async Task<bool> AuthorizeAsync(
        string orgId,
        string userId,
        string permission,
        AuthorizationContext? context,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        // Set org_id session variable for RLS enforcement
        await using (var setOrg = conn.CreateCommand())
        {
            setOrg.CommandText = "SET LOCAL app.current_org_id = @orgId;";
            setOrg.Parameters.AddWithValue("orgId", Guid.Parse(orgId));
            await setOrg.ExecuteNonQueryAsync(ct);
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
        cmd.Parameters.AddWithValue("orgId",        Guid.Parse(orgId));
        cmd.Parameters.AddWithValue("userId",       Guid.Parse(userId));
        cmd.Parameters.AddWithValue("permission",   permission);
        cmd.Parameters.AddWithValue("siteScope",    context?.SiteId is { } site ? $"site:{site}" : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("clusterScope", context?.ClusterId is { } cluster ? $"cluster:{cluster}" : (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    public async Task RequirePermissionAsync(
        string orgId,
        string userId,
        string permission,
        AuthorizationContext? context,
        CancellationToken ct)
    {
        if (!await AuthorizeAsync(orgId, userId, permission, context, ct))
            throw new ModulePermissionDeniedException(orgId, userId, permission);
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(
        string orgId,
        string userId,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

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
        cmd.Parameters.AddWithValue("orgId",  Guid.Parse(orgId));
        cmd.Parameters.AddWithValue("userId", Guid.Parse(userId));

        var permissions = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            permissions.Add(reader.GetString(0));

        return permissions;
    }

    public async Task<bool> AuthorizeResourceAsync(
        string orgId,
        string userId,
        string permission,
        string resourceType,
        string resourceId,
        CancellationToken ct)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);

        await using var cmd = conn.CreateCommand();
        // Resource-scoped check: matches an org-level grant (scope IS NULL) or a
        // grant scoped to this specific resource instance.
        cmd.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM core.role_assignments ra
                JOIN core.role_permissions rp ON rp.role_id = ra.role_id
                WHERE ra.org_id    = @orgId
                  AND ra.user_id   = @userId
                  AND rp.permission IN (@permission, 'platform:admin')
                  AND (ra.expires_at IS NULL OR ra.expires_at > now())
                  AND (ra.scope IS NULL OR ra.scope = @resourceScope)
            )
            """;
        cmd.Parameters.AddWithValue("orgId",         Guid.Parse(orgId));
        cmd.Parameters.AddWithValue("userId",        Guid.Parse(userId));
        cmd.Parameters.AddWithValue("permission",    permission);
        cmd.Parameters.AddWithValue("resourceScope", $"{resourceType}:{resourceId}");

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }
}
