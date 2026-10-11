using Dapper;
using Roma.Domain.Tasks;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Tasks;

public class TaskRepository : ITaskRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TaskRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskItem> AddAsync(TaskItem task, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO tasks (title, day, scheduled_time, category_id, kind, status, started_at, completed_at, duration_seconds, day_closed_at, reminder_sent_at, created_at, updated_at)
            VALUES (@Title, @Day, @ScheduledTime, @CategoryId, @Kind, @Status, @StartedAt, @CompletedAt, @DurationSeconds, @DayClosedAt, @ReminderSentAt, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<int>(sql, new
        {
            task.Title,
            task.Day,
            task.ScheduledTime,
            task.CategoryId,
            task.Kind,
            task.Status,
            StartedAt = task.StartedAt?.ToString("o"),
            CompletedAt = task.CompletedAt?.ToString("o"),
            task.DurationSeconds,
            DayClosedAt = task.DayClosedAt?.ToString("o"),
            task.ReminderSentAt,
            CreatedAt = task.CreatedAt.ToString("o"),
            UpdatedAt = task.UpdatedAt.ToString("o")
        });

        return TaskItem.Reconstitute(
            id,
            task.Title,
            task.Day,
            task.ScheduledTime,
            task.CategoryId,
            task.Kind,
            task.Status,
            task.StartedAt,
            task.CompletedAt,
            task.DurationSeconds,
            task.DayClosedAt,
            task.ReminderSentAt,
            task.CreatedAt,
            task.UpdatedAt);
    }

    public async Task<TaskItem?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, day, scheduled_time, category_id, kind, status, started_at, completed_at, duration_seconds, day_closed_at, reminder_sent_at, created_at, updated_at
            FROM tasks
            WHERE id = @Id AND deleted_at IS NULL;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TaskDbRow>(sql, new { Id = id });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<TaskItem>> GetByDayAsync(string day, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, day, scheduled_time, category_id, kind, status, started_at, completed_at, duration_seconds, day_closed_at, reminder_sent_at, created_at, updated_at
            FROM tasks
            WHERE day = @Day AND deleted_at IS NULL
            ORDER BY scheduled_time ASC, id ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<TaskDbRow>(sql, new { Day = day });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(string? from, string? to, string? date, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, day, scheduled_time, category_id, kind, status, started_at, completed_at, duration_seconds, day_closed_at, reminder_sent_at, created_at, updated_at
            FROM tasks
            WHERE deleted_at IS NULL
              AND (@Date IS NULL OR day = @Date)
              AND (@From IS NULL OR day >= @From)
              AND (@To IS NULL OR day <= @To)
            ORDER BY day ASC, scheduled_time ASC, id ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<TaskDbRow>(sql, new { Date = date, From = from, To = to });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> UpdateAsync(TaskItem task, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tasks
            SET title = @Title,
                day = @Day,
                scheduled_time = @ScheduledTime,
                category_id = @CategoryId,
                status = @Status,
                started_at = @StartedAt,
                completed_at = @CompletedAt,
                duration_seconds = @DurationSeconds,
                day_closed_at = @DayClosedAt,
                reminder_sent_at = @ReminderSentAt,
                updated_at = @UpdatedAt
            WHERE id = @Id AND deleted_at IS NULL;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new
        {
            task.Id,
            task.Title,
            task.Day,
            task.ScheduledTime,
            task.CategoryId,
            task.Status,
            StartedAt = task.StartedAt?.ToString("o"),
            CompletedAt = task.CompletedAt?.ToString("o"),
            task.DurationSeconds,
            DayClosedAt = task.DayClosedAt?.ToString("o"),
            task.ReminderSentAt,
            UpdatedAt = task.UpdatedAt.ToString("o")
        });

        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "UPDATE tasks SET deleted_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id = @Id AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { Id = id });
        return affected > 0;
    }

    public async Task<IReadOnlyList<TaskItem>> FindDueTasksAsync(string day, string currentTime, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, title, day, scheduled_time, category_id, kind, status, started_at, completed_at, duration_seconds, day_closed_at, reminder_sent_at, created_at, updated_at
            FROM tasks
            WHERE kind = 'task'
              AND deleted_at IS NULL
              AND reminder_sent_at IS NULL
              AND status IN ('pending', 'in_progress')
              AND day = @Day
              AND scheduled_time <= @CurrentTime
            ORDER BY scheduled_time ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<TaskDbRow>(sql, new { Day = day, CurrentTime = currentTime });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task MarkReminderSentAsync(int id, string sentAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tasks
            SET reminder_sent_at = @SentAt,
                updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now')
            WHERE id = @Id;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new { Id = id, SentAt = sentAt });
    }

    public async Task<int> RolloverPendingTasksAsync(string today, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE tasks
            SET status = 'not_done',
                day_closed_at = @NowUtc,
                updated_at = @NowUtc
            WHERE kind = 'task'
              AND deleted_at IS NULL
              AND status = 'pending'
              AND day < @Today;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        return await conn.ExecuteAsync(sql, new { Today = today, NowUtc = nowUtc.ToString("o") });
    }

    private class TaskDbRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Day { get; set; } = string.Empty;
        public string Scheduled_Time { get; set; } = string.Empty;
        public int Category_Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Started_At { get; set; }
        public string? Completed_At { get; set; }
        public int? Duration_Seconds { get; set; }
        public string? Day_Closed_At { get; set; }
        public string? Reminder_Sent_At { get; set; }
        public string? Deleted_At { get; set; }
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public TaskItem ToDomain() =>
            TaskItem.Reconstitute(
                Id,
                Title,
                Day,
                Scheduled_Time,
                Category_Id,
                Kind,
                Status,
                Started_At != null ? DateTimeOffset.Parse(Started_At) : null,
                Completed_At != null ? DateTimeOffset.Parse(Completed_At) : null,
                Duration_Seconds,
                Day_Closed_At != null ? DateTimeOffset.Parse(Day_Closed_At) : null,
                Reminder_Sent_At,
                DateTimeOffset.Parse(Created_At),
                DateTimeOffset.Parse(Updated_At));
    }
}
