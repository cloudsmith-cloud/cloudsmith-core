// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

/// <summary>
/// Canonical job environment scope values and normalization
/// (design/api-surface/job-dispatch-contract.md §3 + §5, AB#4839).
/// core.jobs.env is <c>NOT NULL DEFAULT 'default'</c> — a job row never carries a
/// NULL env, and relay routing compares (site_id, env) strictly.
/// </summary>
public static class JobEnvironments
{
    /// <summary>The default environment scope per contract §3.</summary>
    public const string Default = "default";

    /// <summary>
    /// Normalizes a caller-supplied env: null, empty, or whitespace coerces to
    /// <see cref="Default"/>; any other value passes through unchanged.
    /// </summary>
    public static string Normalize(string? env) =>
        string.IsNullOrWhiteSpace(env) ? Default : env;
}

/// <summary>
/// Job record returned by the jobs API.
/// Dispatch fields (<see cref="IdempotencyKey"/>, <see cref="SiteId"/>, <see cref="Env"/>,
/// <see cref="AttemptCount"/>, <see cref="TimeoutAt"/>) per the frozen contract
/// (design/api-surface/job-dispatch-contract.md §3, AB#4839).
/// </summary>
public sealed record JobRecord(
    Guid   JobId,
    Guid   OrgId,
    string JobType,
    string Status,
    Guid?  RunnerId,
    Guid?  ModuleId,
    object? PayloadJson,
    object? ResultJson,
    string? ErrorCode,
    string? ErrorMessage,
    Guid   CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? IdempotencyKey = null,
    Guid?  SiteId = null,
    string? Env = null,
    int    AttemptCount = 0,
    DateTimeOffset? TimeoutAt = null);

/// <summary>
/// Log entry from core.job_log.
/// </summary>
public sealed record JobLogEntry(
    long   LogId,
    Guid   JobId,
    string Severity,
    string Message,
    string? Source,
    DateTimeOffset LoggedAt);

/// <summary>
/// Request to create a new job.
/// Wave 1 dispatch fields per the frozen contract
/// (design/api-surface/job-dispatch-contract.md, AB#4839):
/// <see cref="IdempotencyKey"/> for create-side dedupe (§4.1),
/// <see cref="SiteId"/> + <see cref="Env"/> for (site_id, env) relay routing (§5),
/// and <see cref="TimeoutAt"/> as the absolute deadline for the API timeout watchdog.
/// A job with a null <see cref="SiteId"/> is never routable to a Relay.
/// A null/empty <see cref="Env"/> is coerced to <see cref="JobEnvironments.Default"/>
/// at create time — core.jobs.env is never NULL (§3).
/// </summary>
public sealed record CreateJobRequest(
    string Module,
    string Operation,
    string PayloadJson,
    Guid   CreatedByUserId,
    Guid?  RunnerId = null,
    Guid?  ModuleId = null,
    string? IdempotencyKey = null,
    Guid?  SiteId = null,
    string? Env = null,
    DateTimeOffset? TimeoutAt = null);
