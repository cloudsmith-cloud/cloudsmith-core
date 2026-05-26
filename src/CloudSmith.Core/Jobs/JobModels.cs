// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

/// <summary>
/// Job record returned by the jobs API.
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
    DateTimeOffset? CompletedAt);

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
/// </summary>
public sealed record CreateJobRequest(
    string Module,
    string Operation,
    string PayloadJson,
    Guid   CreatedByUserId,
    Guid?  RunnerId = null,
    Guid?  ModuleId = null);
