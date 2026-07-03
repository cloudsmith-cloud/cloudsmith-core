// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

/// <summary>
/// Service for creating and querying async platform jobs.
/// Jobs are persisted in core.jobs; log entries in core.job_log.
/// </summary>
public interface IJobService
{
    /// <summary>
    /// Creates a new job record in Queued status. Idempotent per the frozen contract
    /// (design/api-surface/job-dispatch-contract.md §4.1, AB#4839): when the request
    /// carries an <c>IdempotencyKey</c> that already exists for the org, the existing
    /// job record is returned and no new row is created.
    /// </summary>
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

    /// <summary>
    /// Atomically transitions a job from one status to another, enforcing the canonical
    /// legal-transition table (design/api-surface/job-dispatch-contract.md §2, AB#4839)
    /// via <see cref="JobStateMachine.CanTransition"/> and a compare-and-set update.
    /// Stamps <c>started_at</c> on → running, <c>completed_at</c> on terminal transitions,
    /// and increments <c>attempt_count</c> on every → dispatched transition.
    /// Returns false when the transition is illegal or the job is not currently in
    /// <paramref name="from"/> (lost race) — never throws for contract violations.
    /// </summary>
    Task<bool> TryTransitionAsync(Guid jobId, string from, string to, CancellationToken ct = default);

    /// <summary>
    /// Persists an agent-reported result and transitions the job to its terminal state
    /// (<c>succeeded</c> or <c>failed</c> per <see cref="JobResult.Succeeded"/>).
    /// Idempotent on <c>jobId</c> per contract §4.3: the result is applied only when the
    /// job is non-terminal; replays and duplicates return false and change nothing.
    /// Not org-scoped — results arrive on the system-context Relay hop keyed by jobId only.
    /// </summary>
    Task<bool> RecordResultAsync(Guid jobId, JobResult result, CancellationToken ct = default);
}
