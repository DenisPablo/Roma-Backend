using Dapper;
using Roma.Domain.Gym;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Gym;

public class WorkoutSessionRepository : IWorkoutSessionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WorkoutSessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<WorkoutSession> AddAsync(WorkoutSession session, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO workout_sessions (date, target_muscle_group, rpe, feeling, recovery_score, notes, created_at, updated_at)
            VALUES (@Date, @TargetMuscleGroup, @Rpe, @Feeling, @RecoveryScore, @Notes, @CreatedAt, @UpdatedAt)
            RETURNING id;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<int>(sql, new
        {
            session.Date,
            session.TargetMuscleGroup,
            session.Rpe,
            session.Feeling,
            session.RecoveryScore,
            session.Notes,
            CreatedAt = session.CreatedAt.ToString("o"),
            UpdatedAt = session.UpdatedAt.ToString("o")
        });

        return WorkoutSession.Reconstitute(
            id,
            session.Date,
            session.TargetMuscleGroup,
            session.Rpe,
            session.Feeling,
            session.RecoveryScore,
            session.Notes,
            session.CreatedAt,
            session.UpdatedAt);
    }

    public async Task<WorkoutSession?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, date, target_muscle_group, rpe, feeling, recovery_score, notes, created_at, updated_at
            FROM workout_sessions
            WHERE id = @Id AND deleted_at IS NULL;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<WorkoutDbRow>(sql, new { Id = id });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<WorkoutSession>> GetSessionsAsync(string? from, string? to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, date, target_muscle_group, rpe, feeling, recovery_score, notes, created_at, updated_at
            FROM workout_sessions
            WHERE deleted_at IS NULL
              AND (@From IS NULL OR date >= @From)
              AND (@To IS NULL OR date <= @To)
            ORDER BY date DESC, id DESC;
            """;

        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<WorkoutDbRow>(sql, new { From = from, To = to });
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "UPDATE workout_sessions SET deleted_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id = @Id AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { Id = id });
        return affected > 0;
    }

    private class WorkoutDbRow
    {
        public int Id { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Target_Muscle_Group { get; set; } = string.Empty;
        public int Rpe { get; set; }
        public int Feeling { get; set; }
        public int Recovery_Score { get; set; }
        public string? Notes { get; set; }
        public string? Deleted_At { get; set; }
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public WorkoutSession ToDomain() =>
            WorkoutSession.Reconstitute(
                Id,
                Date,
                Target_Muscle_Group,
                Rpe,
                Feeling,
                Recovery_Score,
                Notes,
                DateTimeOffset.Parse(Created_At),
                DateTimeOffset.Parse(Updated_At));
    }
}
