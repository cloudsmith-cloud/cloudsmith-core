// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CloudSmith.Core.Setup;

/// <summary>
/// ADR-047 — first-run setup and local (break-glass) authentication.
/// The platform boots with no external IdP; until <see cref="GetStatusAsync"/> reports
/// completed, the operator is sent to the setup wizard. <see cref="CompleteSetupAsync"/>
/// atomically creates the platform org, the first local administrator (PlatformAdmin),
/// its credential, and flips the singleton setup state to Completed.
/// </summary>
public sealed class SetupService
{
    // Fixed built-in role id seeded by M20260519003_SeedBuiltInRoles.
    private const string PlatformAdminRoleId = "00000000-0000-0000-0001-000000000001";

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<SetupService> _logger;

    public SetupService(NpgsqlDataSource db, ILogger<SetupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed record SetupStatus(bool SetupComplete, string? PlatformName, string? PublicUrl);

    public sealed record LocalPrincipal(Guid UserId, Guid OrgId, string Username, string Email, IReadOnlyList<string> Roles);

    public async Task<SetupStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT setup_state, platform_name, public_url FROM core.platform_setup WHERE id = true LIMIT 1";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new SetupStatus(false, null, null);

        var state = reader.GetString(0);
        return new SetupStatus(
            SetupComplete: state == "Completed",
            PlatformName: reader.IsDBNull(1) ? null : reader.GetString(1),
            PublicUrl: reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    /// <summary>
    /// Atomically performs first-run setup. Throws <see cref="InvalidOperationException"/>
    /// if setup is already complete. All inserts + the state flip share one transaction.
    /// </summary>
    public async Task CompleteSetupAsync(
        string platformName,
        string publicUrl,
        string adminUsername,
        string adminEmail,
        string adminPassword,
        string? timezone = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(adminUsername)) throw new ArgumentException("Admin username is required.", nameof(adminUsername));
        if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 8) throw new ArgumentException("Admin password must be at least 8 characters.", nameof(adminPassword));
        if (string.IsNullOrWhiteSpace(platformName)) platformName = "CloudSmith";

        var email = string.IsNullOrWhiteSpace(adminEmail) ? $"{adminUsername}@local" : adminEmail.Trim();
        var passwordHash = HashPassword(adminPassword);

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Guard: lock the singleton row and refuse if already completed (idempotent, race-safe).
        await using (var guard = conn.CreateCommand())
        {
            guard.Transaction = tx;
            guard.CommandText = "SELECT setup_state FROM core.platform_setup WHERE id = true FOR UPDATE";
            var state = (string?)await guard.ExecuteScalarAsync(ct);
            if (state == "Completed")
                throw new InvalidOperationException("Setup has already been completed.");
        }

        // 1. Platform org
        var orgId = await ScalarGuidAsync(conn, tx,
            "INSERT INTO core.orgs (name, slug, tier) VALUES (@name, 'platform', 'standard') RETURNING org_id",
            ct, ("name", platformName));

        // 2. First admin user (local provider — external_id namespaced to avoid IdP collisions)
        var userId = await ScalarGuidAsync(conn, tx,
            """
            INSERT INTO core.users (org_id, external_id, email, display_name, is_active)
            VALUES (@orgId, @externalId, @email, @display, true)
            RETURNING user_id
            """,
            ct, ("orgId", orgId), ("externalId", $"local:{adminUsername.ToLowerInvariant()}"), ("email", email), ("display", adminUsername));

        // 3. Local credential (break-glass)
        await ExecAsync(conn, tx,
            """
            INSERT INTO core.local_credentials (user_id, username, password_hash, is_break_glass)
            VALUES (@userId, @username, @hash, true)
            """,
            ct, ("userId", userId), ("username", adminUsername), ("hash", passwordHash));

        // 4. Assign built-in PlatformAdmin role (self-assigned during bootstrap)
        await ExecAsync(conn, tx,
            """
            INSERT INTO core.role_assignments (org_id, user_id, role_id, assigned_by_user_id)
            VALUES (@orgId, @userId, @roleId::uuid, @userId)
            """,
            ct, ("orgId", orgId), ("userId", userId), ("roleId", PlatformAdminRoleId));

        // 5. Mark setup complete
        await ExecAsync(conn, tx,
            """
            UPDATE core.platform_setup
            SET setup_state = 'Completed', platform_name = @name, public_url = @url,
                timezone = @tz, completed_at = now(), completed_by_user_id = @userId
            WHERE id = true
            """,
            ct, ("name", platformName), ("url", publicUrl ?? string.Empty), ("tz", (object?)timezone ?? DBNull.Value), ("userId", userId));

        await tx.CommitAsync(ct);
        _logger.LogInformation("First-run setup completed. Platform org {OrgId}, admin {UserId}.", orgId, userId);
    }

    /// <summary>
    /// Verifies a local username/password and returns the principal, or null if invalid.
    /// Works regardless of whether an external IdP is configured (break-glass path).
    /// </summary>
    public async Task<LocalPrincipal?> VerifyCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT lc.user_id, lc.password_hash, u.org_id, u.email, lc.username
            FROM core.local_credentials lc
            JOIN core.users u ON u.user_id = lc.user_id
            WHERE lower(lc.username) = lower(@username) AND u.is_active = true
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("username", username);

        Guid userId, orgId; string storedHash, email, actualUsername;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return null;
            userId = reader.GetGuid(0);
            storedHash = reader.GetString(1);
            orgId = reader.GetGuid(2);
            email = reader.GetString(3);
            actualUsername = reader.GetString(4);
        }

        if (!VerifyPassword(password, storedHash)) return null;

        var roles = await GetRoleNamesAsync(conn, userId, ct);

        // Best-effort last-login stamp (non-fatal).
        try
        {
            await ExecAsync(conn, null, "UPDATE core.users SET last_login_at = now() WHERE user_id = @id", ct, ("id", userId));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to update last_login_at for {UserId}", userId); }

        return new LocalPrincipal(userId, orgId, actualUsername, email, roles);
    }

    private static async Task<IReadOnlyList<string>> GetRoleNamesAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT rd.name
            FROM core.role_assignments ra
            JOIN core.role_definitions rd ON rd.role_id = ra.role_id
            WHERE ra.user_id = @userId AND (ra.expires_at IS NULL OR ra.expires_at > now())
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        var roles = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) roles.Add(reader.GetString(0));
        return roles;
    }

    private static async Task<Guid> ScalarGuidAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string, object)[] ps)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, CancellationToken ct, params (string, object)[] ps)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // --- Password hashing (PBKDF2-SHA256; format: pbkdf2$<iterations>$<salt_b64>$<hash_b64>) ---

    private const int Pbkdf2Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
