namespace Roma.Domain.Dashboard;

public record RawTaskStats(
    long TotalTasks,
    long CompletedTasks,
    long PendingTasks,
    long InProgressTasks,
    long NotDoneTasks,
    long TotalDurationSeconds);

public record RawCategoryStats(
    long CategoryId,
    string CategoryName,
    long TotalTasks,
    long CompletedTasks,
    long PendingTasks,
    long TotalDurationSeconds);

public record RawWorkoutItem(
    long Id,
    string Date,
    string TargetMuscleGroup,
    long Rpe,
    long Feeling,
    long RecoveryScore,
    long? DurationMinutes);

public record RawDailyTaskCount(
    string Date,
    long TasksTotal,
    long TasksCompleted,
    long TasksPending);

public interface IDashboardRepository
{
    Task<RawTaskStats> GetTaskStatsAsync(string from, string to, CancellationToken ct = default);
    Task<IReadOnlyList<RawCategoryStats>> GetCategoryStatsAsync(string from, string to, CancellationToken ct = default);
    Task<IReadOnlyList<RawWorkoutItem>> GetWorkoutsAsync(string from, string to, CancellationToken ct = default);
    Task<IReadOnlyList<RawDailyTaskCount>> GetDailyTasksAsync(string from, string to, CancellationToken ct = default);
}
