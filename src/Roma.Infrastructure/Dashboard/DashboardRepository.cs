using Dapper;
using Roma.Domain.Dashboard;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Dashboard;

public class DashboardRepository : IDashboardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RawTaskStats> GetTaskStatsAsync(string from, string to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalTasks,
                COALESCE(SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END), 0) AS CompletedTasks,
                COALESCE(SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END), 0) AS PendingTasks,
                COALESCE(SUM(CASE WHEN status = 'in_progress' THEN 1 ELSE 0 END), 0) AS InProgressTasks,
                COALESCE(SUM(CASE WHEN status = 'not_done' THEN 1 ELSE 0 END), 0) AS NotDoneTasks,
                COALESCE(SUM(CASE WHEN status = 'completed' THEN duration_seconds ELSE 0 END), 0) AS TotalDurationSeconds
            FROM tasks
            WHERE deleted_at IS NULL
              AND day >= @From AND day <= @To;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var result = await conn.QuerySingleOrDefaultAsync<RawTaskStats>(sql, new { From = from, To = to });
        return result ?? new RawTaskStats(0, 0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<RawCategoryStats>> GetCategoryStatsAsync(string from, string to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                c.id AS CategoryId,
                c.name AS CategoryName,
                COUNT(t.id) AS TotalTasks,
                COALESCE(SUM(CASE WHEN t.status = 'completed' THEN 1 ELSE 0 END), 0) AS CompletedTasks,
                COALESCE(SUM(CASE WHEN t.status IN ('pending', 'in_progress') THEN 1 ELSE 0 END), 0) AS PendingTasks,
                COALESCE(SUM(CASE WHEN t.status = 'completed' THEN t.duration_seconds ELSE 0 END), 0) AS TotalDurationSeconds
            FROM categories c
            LEFT JOIN tasks t ON t.category_id = c.id
                             AND t.deleted_at IS NULL
                             AND t.day >= @From AND t.day <= @To
            WHERE c.deleted_at IS NULL
            GROUP BY c.id, c.name
            ORDER BY c.id ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<RawCategoryStats>(sql, new { From = from, To = to });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RawWorkoutItem>> GetWorkoutsAsync(string from, string to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id AS Id,
                date AS Date,
                target_muscle_group AS TargetMuscleGroup,
                rpe AS Rpe,
                feeling AS Feeling,
                recovery_score AS RecoveryScore
            FROM workout_sessions
            WHERE deleted_at IS NULL
              AND date >= @From AND date <= @To
            ORDER BY date ASC, id ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<RawWorkoutItem>(sql, new { From = from, To = to });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RawDailyTaskCount>> GetDailyTasksAsync(string from, string to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                day AS Date,
                COUNT(*) AS TasksTotal,
                COALESCE(SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END), 0) AS TasksCompleted,
                COALESCE(SUM(CASE WHEN status IN ('pending', 'in_progress') THEN 1 ELSE 0 END), 0) AS TasksPending
            FROM tasks
            WHERE deleted_at IS NULL
              AND day >= @From AND day <= @To
            GROUP BY day
            ORDER BY day ASC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<RawDailyTaskCount>(sql, new { From = from, To = to });
        return rows.ToList();
    }
}
