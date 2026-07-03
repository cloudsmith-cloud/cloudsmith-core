// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

namespace CloudSmith.Core.Jobs;

/// <summary>
/// Canonical <c>core.jobs.status</c> values (snake_case, as persisted).
/// </summary>
public static class JobStatuses
{
    /// <summary>Accepted — the API persisted the job row; nothing has been sent anywhere yet.</summary>
    public const string Queued = "queued";

    /// <summary>Routed — a Relay durably acked the frame; the job awaits agent poll.</summary>
    public const string Dispatched = "dispatched";

    /// <summary>Executing — the Agent picked the job up and started the payload.</summary>
    public const string Running = "running";

    /// <summary>Terminal — the payload completed with success.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Terminal — the payload failed, or a pre-execution failure occurred.</summary>
    public const string Failed = "failed";

    /// <summary>Terminal — the API watchdog adjudicated a timeout (<c>timeout_at</c> passed).</summary>
    public const string TimedOut = "timed_out";

    /// <summary>Terminal — cancelled before execution started.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>All statuses, in lifecycle order.</summary>
    public static readonly IReadOnlyList<string> All =
        [Queued, Dispatched, Running, Succeeded, Failed, TimedOut, Cancelled];

    /// <summary>Returns true when <paramref name="status"/> is terminal — no transitions out, ever.</summary>
    public static bool IsTerminal(string status) =>
        status is Succeeded or Failed or TimedOut or Cancelled;
}

/// <summary>
/// The canonical job state machine legal-transition table
/// (design/api-surface/job-dispatch-contract.md §2, AB#4839).
/// <code>
/// queued → dispatched → running → succeeded | failed | timed_out
/// queued/dispatched → cancelled
/// dispatched → queued            (requeue; increments attempt_count)
/// queued/dispatched → failed     (pre-execution failures only)
/// dispatched → timed_out         (API watchdog)
/// </code>
/// Terminal states (<c>succeeded</c>, <c>failed</c>, <c>timed_out</c>, <c>cancelled</c>) are final.
/// </summary>
public static class JobStateMachine
{
    private static readonly HashSet<(string From, string To)> Legal =
    [
        // queued: accepted, nothing sent yet
        (JobStatuses.Queued,     JobStatuses.Dispatched),
        (JobStatuses.Queued,     JobStatuses.Failed),     // pre-execution failure (no eligible relay/agent, retries exhausted)
        (JobStatuses.Queued,     JobStatuses.Cancelled),

        // dispatched: routed to a relay queue
        (JobStatuses.Dispatched, JobStatuses.Queued),     // requeue — relay disconnected before ack / API restart rescan
        (JobStatuses.Dispatched, JobStatuses.Running),
        (JobStatuses.Dispatched, JobStatuses.Failed),     // pre-execution failure (ackStatus=rejected)
        (JobStatuses.Dispatched, JobStatuses.TimedOut),   // API watchdog
        (JobStatuses.Dispatched, JobStatuses.Cancelled),

        // running: executing on an agent
        (JobStatuses.Running,    JobStatuses.Succeeded),
        (JobStatuses.Running,    JobStatuses.Failed),
        (JobStatuses.Running,    JobStatuses.TimedOut),   // API watchdog
    ];

    /// <summary>
    /// Returns true when the transition <paramref name="from"/> → <paramref name="to"/>
    /// is legal per the canonical table. Self-transitions and transitions out of
    /// terminal states are always illegal.
    /// </summary>
    public static bool CanTransition(string from, string to) => Legal.Contains((from, to));

    /// <summary>All legal transitions as (from, to) pairs.</summary>
    public static IReadOnlyCollection<(string From, string To)> LegalTransitions => Legal;
}
