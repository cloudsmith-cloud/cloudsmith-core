// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

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
