namespace Roma.Domain.Gym;

public interface IWorkoutSessionRepository
{
    Task<WorkoutSession> AddAsync(WorkoutSession session, CancellationToken ct = default);
    Task<WorkoutSession?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkoutSession>> GetSessionsAsync(string? from, string? to, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
