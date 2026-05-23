// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CloudSmith.Core.Tests.Migrations;

[Trait("Category", "Integration")]
public sealed class MigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public void Migrations_RunClean_AllTablesPresent()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCloudSmithCore(_pg.GetConnectionString())
            .BuildServiceProvider();

        services.MigrateCloudSmithDatabase();

        // Verify core schema tables exist
        using var conn = new Npgsql.NpgsqlConnection(_pg.GetConnectionString());
        conn.Open();

        var tables = new[]
        {
            "core.orgs", "core.users", "core.role_definitions", "core.role_permissions",
            "core.role_assignments", "core.module_registry", "core.sites", "core.runner_registry",
            "core.jobs", "core.audit_log", "core.notifications", "core.secret_refs",
            "config.scopes", "config.variable_schema", "config.variable_values",
            "config.variable_history", "config.resources", "config.deployment_snapshots",
            "config.drift_findings", "config.stale_findings", "config.scope_group_activation"
        };

        foreach (var table in tables)
        {
            var parts = table.Split('.');
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table";
            cmd.Parameters.AddWithValue("schema", parts[0]);
            cmd.Parameters.AddWithValue("table", parts[1]);
            var count = (long)cmd.ExecuteScalar()!;
            Assert.True(count == 1, $"Table {table} was not created by migration");
        }
    }

    [Fact]
    public void Migrations_RunTwice_Idempotent()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCloudSmithCore(_pg.GetConnectionString())
            .BuildServiceProvider();

        services.MigrateCloudSmithDatabase();
        // Running a second time must not throw
        services.MigrateCloudSmithDatabase();
    }

    [Fact]
    public void SeedMigration_BuiltInRoles_AllPresent()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCloudSmithCore(_pg.GetConnectionString())
            .BuildServiceProvider();

        services.MigrateCloudSmithDatabase();

        using var conn = new Npgsql.NpgsqlConnection(_pg.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM core.role_definitions WHERE is_built_in = true";
        var count = (long)cmd.ExecuteScalar()!;
        Assert.Equal(4L, count);
    }
}
