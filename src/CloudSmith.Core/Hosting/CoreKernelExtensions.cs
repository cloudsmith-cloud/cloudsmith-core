// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Authorization;
using CloudSmith.Core.Config;
using CloudSmith.Core.Events;
using CloudSmith.Core.Health;
using CloudSmith.Core.Migrations;
using CloudSmith.Sdk.Authorization;
using CloudSmith.Sdk.Config;
using CloudSmith.Sdk.Events;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CloudSmith.Core.Hosting;

public static class CoreKernelExtensions
{
    /// <summary>
    /// Registers all CloudSmith platform kernel services and runs pending database migrations.
    /// Call this in Program.cs before building the host.
    /// </summary>
    public static IServiceCollection AddCloudSmithCore(this IServiceCollection services, string connectionString)
    {
        // PostgreSQL connection pool
        var dataSource = NpgsqlDataSource.Create(connectionString);
        services.AddSingleton(dataSource);

        // Platform services
        services.AddSingleton<IPlatformEventBus, InProcessEventBus>();
        services.AddScoped<ICloudSmithAuthorizationService, PostgresAuthorizationService>();
        services.AddScoped<ICloudSmithConfigService, PostgresConfigService>();

        // FluentMigrator
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(M20260519001_CreateCoreSchema).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        // Health checks — /health/live (always up) and /health/ready (DB required)
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Runs all pending FluentMigrator migrations. Call after app.Build() and before app.Run().
    /// </summary>
    public static void MigrateCloudSmithDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }
}
