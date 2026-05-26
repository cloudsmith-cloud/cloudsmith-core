// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

/// <summary>
/// Service for creating and querying async platform jobs.
/// Jobs are persisted in core.jobs; log entries in core.job_log.
/// </summary>
public interface IJobService
{
    /// <summary>Creates a new job record in Queued status.</summary>
    Task<JobRecord> CreateJobAsync(Guid orgId, CreateJobRequest request, CancellationToken ct = default);

    /// <summary>Gets a job record by ID scoped to the caller's org.</summary>
    Task<JobRecord?> GetJobAsync(Guid jobId, Guid orgId, CancellationToken ct = default);

    /// <summary>Retrieves paginated log entries for a job.</summary>
    Task<(IReadOnlyList<JobLogEntry> Items, int TotalItems)> GetJobLogAsync(
        Guid jobId, Guid orgId, string? severity, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Appends a log entry to a job.</summary>
    Task AppendLogAsync(Guid jobId, Guid orgId, string severity, string message, string? source, CancellationToken ct = default);

    /// <summary>Updates a job's status and optional result/error fields.</summary>
    Task UpdateJobStatusAsync(Guid jobId, Guid orgId, string status,
        string? resultJson = null, string? errorCode = null, string? errorMessage = null,
        CancellationToken ct = default);
}
