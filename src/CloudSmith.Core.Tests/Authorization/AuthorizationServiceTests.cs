// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Authorization;
using CloudSmith.Core.Hosting;
using CloudSmith.Sdk.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CloudSmith.Core.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class AuthorizationServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private NpgsqlDataSource _db = null!;
    private static readonly Guid OrgAId = Guid.NewGuid();
    private static readonly Guid OrgBId = Guid.NewGuid();
    private static readonly Guid AliceId = Guid.NewGuid();
    private static readonly Guid BobId = Guid.NewGuid();
    private static readonly Guid OrgAdminRoleId = new("00000000-0000-0000-0001-000000000002");

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _db = NpgsqlDataSource.Create(_pg.GetConnectionString());

        var services = new ServiceCollection().AddLogging().AddCloudSmithCore(_pg.GetConnectionString()).BuildServiceProvider();
        services.MigrateCloudSmithDatabase();

        // Seed two orgs, two users, and one role assignment
        await using var conn = await _db.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO core.orgs (org_id, name, slug) VALUES
                ('{OrgAId}', 'Org A', 'org-a'),
                ('{OrgBId}', 'Org B', 'org-b');

            INSERT INTO core.users (user_id, org_id, external_id, email, display_name) VALUES
                ('{AliceId}', '{OrgAId}', 'alice-sub', 'alice@example.com', 'Alice'),
                ('{BobId}',   '{OrgBId}', 'bob-sub',   'bob@example.com',   'Bob');

            -- Alice is OrgAdmin in OrgA
            INSERT INTO core.role_assignments (org_id, user_id, role_id, assigned_by_user_id) VALUES
                ('{OrgAId}', '{AliceId}', '{OrgAdminRoleId}', '{AliceId}');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task OrgAdmin_HasClusterWrite_InOwnOrg()
    {
        var svc = new PostgresAuthorizationService(_db, NullLogger<PostgresAuthorizationService>.Instance);
        var result = await svc.AuthorizeAsync(OrgAId.ToString(), AliceId.ToString(), "cluster:write", null, CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task OrgAdmin_DoesNotHavePermission_InOtherOrg()
    {
        var svc = new PostgresAuthorizationService(_db, NullLogger<PostgresAuthorizationService>.Instance);
        // Alice has OrgAdmin in OrgA but is being checked against OrgB — tenant isolation
        var result = await svc.AuthorizeAsync(OrgBId.ToString(), AliceId.ToString(), "cluster:write", null, CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task UserWithNoRole_HasNoPermissions()
    {
        var svc = new PostgresAuthorizationService(_db, NullLogger<PostgresAuthorizationService>.Instance);
        var result = await svc.AuthorizeAsync(OrgBId.ToString(), BobId.ToString(), "cluster:read", null, CancellationToken.None);
        Assert.False(result);
    }
}
