using Dapper;
using Roma.Domain.Common;
using Roma.Domain.Reminders;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Reminders;

public class ReminderRepository : IReminderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ReminderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateForEventAsync(int taskId, int[] offsets, string eventDay, string eventTime, CancellationToken ct = default)
    {
        if (offsets == null || offsets.Length == 0) return;

        var moment = LocalParts.FromStrings(eventDay, eventTime);
        const string sql = """
            INSERT OR IGNORE INTO task_reminders (task_id, offset_minutes, due_at, created_at, updated_at)
            VALUES (@TaskId, @OffsetMinutes, @DueAt, @CreatedAt, @UpdatedAt);
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        foreach (var offset in offsets)
        {
            var dueAt = TimeUtils.CalcDueAt(moment, offset);
            await conn.ExecuteAsync(sql, new
            {
                TaskId = taskId,
                OffsetMinutes = offset,
                DueAt = dueAt,
                CreatedAt = nowIso,
                UpdatedAt = nowIso
            }, transaction: tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task ReplaceForEventAsync(int taskId, int[] offsets, string eventDay, string eventTime, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        const string deleteSql = "DELETE FROM task_reminders WHERE task_id = @TaskId;";
        await conn.ExecuteAsync(deleteSql, new { TaskId = taskId }, transaction: tx);

        if (offsets != null && offsets.Length > 0)
        {
            var moment = LocalParts.FromStrings(eventDay, eventTime);
            const string insertSql = """
                INSERT OR IGNORE INTO task_reminders (task_id, offset_minutes, due_at, created_at, updated_at)
                VALUES (@TaskId, @OffsetMinutes, @DueAt, @CreatedAt, @UpdatedAt);
                """;

            var nowIso = DateTimeOffset.UtcNow.ToString("o");
            foreach (var offset in offsets)
            {
                var dueAt = TimeUtils.CalcDueAt(moment, offset);
                await conn.ExecuteAsync(insertSql, new
                {
                    TaskId = taskId,
                    OffsetMinutes = offset,
                    DueAt = dueAt,
                    CreatedAt = nowIso,
                    UpdatedAt = nowIso
                }, transaction: tx);
            }
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<TaskReminder>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, task_id, offset_minutes, due_at, sent_at, created_at, updated_at
            FROM task_reminders
            WHERE task_id = @TaskId
            ORDER BY offset_minutes ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<ReminderDbRow>(sql, new { TaskId = taskId });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<TaskReminder>> FindDueRemindersAsync(string nowLocal, CancellationToken ct = default)
    {
        // P2 Policy: Unsent reminders, due <= nowLocal, picking MAX(due_at) per event task
        const string sql = """
            WITH due_candidates AS (
                SELECT r.id, r.task_id, r.offset_minutes, r.due_at, r.sent_at, r.created_at, r.updated_at,
                       ROW_NUMBER() OVER (PARTITION BY r.task_id ORDER BY r.due_at DESC, r.id DESC) as rn
                FROM task_reminders r
                JOIN tasks t ON r.task_id = t.id
                WHERE t.kind = 'event'
                  AND t.deleted_at IS NULL
                  AND r.sent_at IS NULL
                  AND r.due_at <= @NowLocal
            )
            SELECT id, task_id, offset_minutes, due_at, sent_at, created_at, updated_at
            FROM due_candidates
            WHERE rn = 1
            ORDER BY due_at ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<ReminderDbRow>(sql, new { NowLocal = nowLocal });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task MarkSentAsync(int reminderId, string sentAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE task_reminders
            SET sent_at = @SentAt,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE id = @Id;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new { Id = reminderId, SentAt = sentAt });
    }

    public async Task RecomputeForEventAsync(int taskId, string newDay, string newTime, string nowLocal, CancellationToken ct = default)
    {
        var existing = await GetByTaskIdAsync(taskId, ct);
        if (existing.Count == 0) return;

        var moment = LocalParts.FromStrings(newDay, newTime);
        const string sql = """
            UPDATE task_reminders
            SET due_at = @DueAt,
                sent_at = @SentAt,
                updated_at = @UpdatedAt
            WHERE id = @Id;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        foreach (var rem in existing)
        {
            var newDueAt = TimeUtils.CalcDueAt(moment, rem.OffsetMinutes);
            // Clear sent_at only where new due_at is in the future
            var newSentAt = string.CompareOrdinal(newDueAt, nowLocal) > 0 ? null : rem.SentAt;

            await conn.ExecuteAsync(sql, new
            {
                Id = rem.Id,
                DueAt = newDueAt,
                SentAt = newSentAt,
                UpdatedAt = nowIso
            }, transaction: tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task MarkOverdueBeforeBootAsync(string bootLocal, DateTimeOffset bootUtc, CancellationToken ct = default)
    {
        var bootUtcIso = bootUtc.ToString("o");

        const string sqlReminders = """
            UPDATE task_reminders
            SET sent_at = @BootLocal,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE sent_at IS NULL
              AND due_at <= @BootLocal
              AND created_at <= @BootUtcIso;
            """;

        var bootParts = bootLocal.Split('T');
        var bootDate = bootParts[0];
        var bootTime = bootParts.Length > 1 ? bootParts[1] : "00:00";

        const string sqlTasks = """
            UPDATE tasks
            SET reminder_sent_at = @BootLocal,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE kind = 'task'
              AND reminder_sent_at IS NULL
              AND (day < @BootDate OR (day = @BootDate AND scheduled_time <= @BootTime))
              AND created_at <= @BootUtcIso;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(sqlReminders, new { BootLocal = bootLocal, BootUtcIso = bootUtcIso }, transaction: tx);
        await conn.ExecuteAsync(sqlTasks, new { BootLocal = bootLocal, BootDate = bootDate, BootTime = bootTime, BootUtcIso = bootUtcIso }, transaction: tx);

        await tx.CommitAsync(ct);
    }

    private class ReminderDbRow
    {
        public int Id { get; set; }
        public int Task_Id { get; set; }
        public int Offset_Minutes { get; set; }
        public string Due_At { get; set; } = string.Empty;
        public string? Sent_At { get; set; }
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public TaskReminder ToDomain() =>
            TaskReminder.Reconstitute(
                Id,
                Task_Id,
                Offset_Minutes,
                Due_At,
                Sent_At,
                DateTimeOffset.Parse(Created_At),
                DateTimeOffset.Parse(Updated_At));
    }
}
