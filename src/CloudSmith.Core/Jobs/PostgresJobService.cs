// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Npgsql;

namespace CloudSmith.Core.Jobs;

/// <summary>
/// PostgreSQL-backed implementation of IJobService.
/// Reads/writes core.jobs and core.job_log. State transitions enforce the canonical
/// legal-transition table (design/api-surface/job-dispatch-contract.md §2, AB#4839).
/// </summary>
public sealed class PostgresJobService : IJobService
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerDefaults.Web);

    private const string JobColumns = """
        job_id, org_id, job_type, status, runner_id, module_id,
        payload_json, result_json, error_code, error_message,
        created_by_user_id, created_at, started_at, completed_at,
        idempotency_key, site_id, env, attempt_count, timeout_at
        """;

    private readonly NpgsqlDataSource _db;

    public PostgresJobService(NpgsqlDataSource db) => _db = db;

    public async Task<JobRecord> CreateJobAsync(Guid orgId, CreateJobRequest request, CancellationToken ct = default)
    {
        // Contract §4.1: ON CONFLICT (org_id, idempotency_key) DO NOTHING — a duplicate
        // key inserts no row; the existing job is then returned instead (retry-safe create).
        const string insertSql = $"""
            INSERT INTO core.jobs
                (org_id, runner_id, module_id, job_type, status, payload_json, created_by_user_id,
                 idempotency_key, site_id, env, timeout_at)
            VALUES
                (@org_id, @runner_id, @module_id, @job_type, 'queued', @payload::jsonb, @created_by,
                 @idempotency_key, @site_id, @env, @timeout_at)
            ON CONFLICT (org_id, idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING
            RETURNING
                {JobColumns}
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand(insertSql, conn))
        {
            cmd.Parameters.AddWithValue("@org_id",    orgId);
            cmd.Parameters.AddWithValue("@runner_id", (object?)request.RunnerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@module_id", (object?)request.ModuleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@job_type",  request.Operation);
            cmd.Parameters.AddWithValue("@payload",   request.PayloadJson);
            cmd.Parameters.AddWithValue("@created_by", request.CreatedByUserId);
            cmd.Parameters.AddWithValue("@idempotency_key", (object?)request.IdempotencyKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@site_id",   (object?)request.SiteId ?? DBNull.Value);
            // Contract §3: core.jobs.env is NOT NULL DEFAULT 'default' — never write NULL.
            cmd.Parameters.AddWithValue("@env",       JobEnvironments.Normalize(request.Env));
            cmd.Parameters.AddWithValue("@timeout_at",(object?)request.TimeoutAt ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return ReadJob(reader);
        }

        // Conflict path: fetch the job the idempotency key already points at.
        const string existingSql = $"""
            SELECT {JobColumns}
            FROM core.jobs
            WHERE org_id = @org_id AND idempotency_key = @idempotency_key
            """;

        await using var existingCmd = new NpgsqlCommand(existingSql, conn);
        existingCmd.Parameters.AddWithValue("@org_id", orgId);
        existingCmd.Parameters.AddWithValue("@idempotency_key", request.IdempotencyKey!);
        await using var existingReader = await existingCmd.ExecuteReaderAsync(ct);
        await existingReader.ReadAsync(ct);
        return ReadJob(existingReader);
    }

    public async Task<JobRecord?> GetJobAsync(Guid jobId, Guid orgId, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {JobColumns}
            FROM core.jobs
            WHERE job_id = @job_id AND org_id = @org_id
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@job_id", jobId);
        cmd.Parameters.AddWithValue("@org_id", orgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async Task<(IReadOnlyList<JobLogEntry> Items, int TotalItems)> GetJobLogAsync(
        Guid jobId, Guid orgId, string? severity, int page, int pageSize, CancellationToken ct = default)
    {
        var severityFilter = string.IsNullOrWhiteSpace(severity) ? "" : "AND severity = @severity";
        var countSql = $"""
            SELECT COUNT(*)
            FROM core.job_log
            WHERE job_id = @job_id AND org_id = @org_id
            {severityFilter}
            """;
        var querySql = $"""
            SELECT log_id, job_id, severity, message, source, logged_at
            FROM core.job_log
            WHERE job_id = @job_id AND org_id = @org_id
            {severityFilter}
            ORDER BY logged_at
            LIMIT @limit OFFSET @offset
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);

        await using var countCmd = new NpgsqlCommand(countSql, conn);
        countCmd.Parameters.AddWithValue("@job_id", jobId);
        countCmd.Parameters.AddWithValue("@org_id", orgId);
        if (!string.IsNullOrWhiteSpace(severity)) countCmd.Parameters.AddWithValue("@severity", severity);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct) ?? 0);

        await using var queryCmd = new NpgsqlCommand(querySql, conn);
        queryCmd.Parameters.AddWithValue("@job_id", jobId);
        queryCmd.Parameters.AddWithValue("@org_id", orgId);
        if (!string.IsNullOrWhiteSpace(severity)) queryCmd.Parameters.AddWithValue("@severity", severity);
        queryCmd.Parameters.AddWithValue("@limit",  pageSize);
        queryCmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

        var items = new List<JobLogEntry>();
        await using var reader = await queryCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new JobLogEntry(
                LogId:    reader.GetInt64(0),
                JobId:    reader.GetGuid(1),
                Severity: reader.GetString(2),
                Message:  reader.GetString(3),
                Source:   reader.IsDBNull(4) ? null : reader.GetString(4),
                LoggedAt: reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return (items, total);
    }

    public async Task AppendLogAsync(Guid jobId, Guid orgId, string severity, string message, string? source, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO core.job_log (job_id, org_id, severity, message, source)
            VALUES (@job_id, @org_id, @severity, @message, @source)
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@job_id",   jobId);
        cmd.Parameters.AddWithValue("@org_id",   orgId);
        cmd.Parameters.AddWithValue("@severity", severity);
        cmd.Parameters.AddWithValue("@message",  message);
        cmd.Parameters.AddWithValue("@source",   (object?)source ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobStatusAsync(Guid jobId, Guid orgId, string status,
        string? resultJson = null, string? errorCode = null, string? errorMessage = null,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE core.jobs
            SET    status        = @status,
                   result_json   = COALESCE(@result::jsonb, result_json),
                   error_code    = COALESCE(@error_code, error_code),
                   error_message = COALESCE(@error_message, error_message),
                   started_at    = CASE WHEN @status = 'running'   AND started_at IS NULL THEN now() ELSE started_at END,
                   completed_at  = CASE WHEN @status IN ('succeeded','failed','cancelled','timed_out') THEN now() ELSE completed_at END
            WHERE  job_id = @job_id AND org_id = @org_id
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@job_id",       jobId);
        cmd.Parameters.AddWithValue("@org_id",       orgId);
        cmd.Parameters.AddWithValue("@status",       status);
        cmd.Parameters.AddWithValue("@result",       (object?)resultJson       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_code",   (object?)errorCode        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message",(object?)errorMessage     ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> TryTransitionAsync(Guid jobId, string from, string to, CancellationToken ct = default)
    {
        // Contract §2: illegal transitions are refused in code before touching the DB;
        // the compare-and-set WHERE clause guards against lost races.
        if (!JobStateMachine.CanTransition(from, to))
            return false;

        const string sql = """
            UPDATE core.jobs
            SET    status        = @to,
                   started_at    = CASE WHEN @to = 'running' AND started_at IS NULL THEN now() ELSE started_at END,
                   completed_at  = CASE WHEN @to IN ('succeeded','failed','timed_out','cancelled') THEN now() ELSE completed_at END,
                   attempt_count = attempt_count + CASE WHEN @to = 'dispatched' THEN 1 ELSE 0 END
            WHERE  job_id = @job_id AND status = @from
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@job_id", jobId);
        cmd.Parameters.AddWithValue("@from",   from);
        cmd.Parameters.AddWithValue("@to",     to);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> RecordResultAsync(Guid jobId, JobResult result, CancellationToken ct = default)
    {
        // Contract §4.3: idempotent on jobId — the result applies only while the job is
        // non-terminal; duplicates and replays match zero rows and return false.
        // completed_at comes from the agent's CompletedAt stamp (contract §1.3), and
        // started_at is backfilled when the running signal was lost (sequence step 12–15 note).
        const string sql = """
            UPDATE core.jobs
            SET    status        = @to,
                   result_json   = @result::jsonb,
                   error_code    = CASE WHEN @succeeded THEN error_code ELSE 'job-failed' END,
                   error_message = COALESCE(@error, error_message),
                   started_at    = COALESCE(started_at, now()),
                   completed_at  = @completed_at
            WHERE  job_id = @job_id
              AND  status NOT IN ('succeeded','failed','timed_out','cancelled')
            """;

        var to = result.Succeeded ? JobStatuses.Succeeded : JobStatuses.Failed;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@job_id",       jobId);
        cmd.Parameters.AddWithValue("@to",           to);
        cmd.Parameters.AddWithValue("@succeeded",    result.Succeeded);
        cmd.Parameters.AddWithValue("@result",       JsonSerializer.Serialize(result, ResultJsonOptions));
        cmd.Parameters.AddWithValue("@error",        (object?)result.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completed_at", result.CompletedAt);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private static JobRecord ReadJob(NpgsqlDataReader r) => new(
        JobId:            r.GetGuid(0),
        OrgId:            r.GetGuid(1),
        JobType:          r.GetString(2),
        Status:           r.GetString(3),
        RunnerId:         r.IsDBNull(4)  ? null : r.GetGuid(4),
        ModuleId:         r.IsDBNull(5)  ? null : r.GetGuid(5),
        PayloadJson:      r.IsDBNull(6)  ? null : r.GetValue(6),
        ResultJson:       r.IsDBNull(7)  ? null : r.GetValue(7),
        ErrorCode:        r.IsDBNull(8)  ? null : r.GetString(8),
        ErrorMessage:     r.IsDBNull(9)  ? null : r.GetString(9),
        CreatedByUserId:  r.GetGuid(10),
        CreatedAt:        r.GetFieldValue<DateTimeOffset>(11),
        StartedAt:        r.IsDBNull(12) ? null : r.GetFieldValue<DateTimeOffset>(12),
        CompletedAt:      r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13),
        IdempotencyKey:   r.IsDBNull(14) ? null : r.GetString(14),
        SiteId:           r.IsDBNull(15) ? null : r.GetGuid(15),
        Env:              r.IsDBNull(16) ? null : r.GetString(16),
        AttemptCount:     r.GetInt32(17),
        TimeoutAt:        r.IsDBNull(18) ? null : r.GetFieldValue<DateTimeOffset>(18));
}
