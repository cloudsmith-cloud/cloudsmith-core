// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using CloudSmith.Core.Authorization;
using CloudSmith.Core.Config;
using CloudSmith.Core.Events;
using CloudSmith.Core.Health;
using CloudSmith.Core.Jobs;
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
        // InProcessEventBus is registered as both Singleton and IPlatformEventBus so
        // OutboxWorker can depend on the concrete type for direct publish calls.
        services.AddSingleton<InProcessEventBus>();
        services.AddSingleton<IPlatformEventBus>(sp => sp.GetRequiredService<InProcessEventBus>());
        services.AddScoped<ICloudSmithAuthorizationService, PostgresAuthorizationService>();
        services.AddScoped<ICloudSmithConfigService, PostgresConfigService>();

        // Event outbox — durable at-least-once event delivery (AB#1425).
        services.AddSingleton<OutboxPublisher>();
        services.AddHostedService<OutboxWorker>();

        // ADR-047 first-run setup + local (break-glass) authentication
        services.AddScoped<Setup.SetupService>();

        // Job service — persists and queries async platform jobs (AB#1429)
        services.AddScoped<IJobService, PostgresJobService>();

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
