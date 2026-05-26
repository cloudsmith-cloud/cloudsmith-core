// Copyright 2026 CloudSmith Contributors
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace CloudSmith.Core.Jobs;

/// <summary>
/// PostgreSQL-backed implementation of IJobService.
/// Reads/writes core.jobs and core.job_log.
/// </summary>
public sealed class PostgresJobService : IJobService
{
    private readonly NpgsqlDataSource _db;

    public PostgresJobService(NpgsqlDataSource db) => _db = db;

    public async Task<JobRecord> CreateJobAsync(Guid orgId, CreateJobRequest request, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO core.jobs
                (org_id, runner_id, module_id, job_type, status, payload_json, created_by_user_id)
            VALUES
                (@org_id, @runner_id, @module_id, @job_type, 'queued', @payload::jsonb, @created_by)
            RETURNING
                job_id, org_id, job_type, status, runner_id, module_id,
                payload_json, result_json, error_code, error_message,
                created_by_user_id, created_at, started_at, completed_at
            """;

        await using var conn = await _db.OpenConnectionAsync(ct);
        await using var cmd  = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@org_id",    orgId);
        cmd.Parameters.AddWithValue("@runner_id", (object?)request.RunnerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@module_id", (object?)request.ModuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@job_type",  request.Operation);
        cmd.Parameters.AddWithValue("@payload",   request.PayloadJson);
        cmd.Parameters.AddWithValue("@created_by", request.CreatedByUserId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return ReadJob(reader);
    }

    public async Task<JobRecord?> GetJobAsync(Guid jobId, Guid orgId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT job_id, org_id, job_type, status, runner_id, module_id,
                   payload_json, result_json, error_code, error_message,
                   created_by_user_id, created_at, started_at, completed_at
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
        CompletedAt:      r.IsDBNull(13) ? null : r.GetFieldValue<DateTimeOffset>(13));
}
