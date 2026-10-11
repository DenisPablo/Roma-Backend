namespace Roma.Domain.Tasks;

public interface ITaskRepository
{
    Task<TaskItem> AddAsync(TaskItem task, CancellationToken ct = default);
    Task<TaskItem?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetByDayAsync(string day, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetTasksAsync(string? from, string? to, string? date, CancellationToken ct = default);
    Task<bool> UpdateAsync(TaskItem task, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> FindDueTasksAsync(string day, string currentTime, CancellationToken ct = default);
    Task MarkReminderSentAsync(int id, string sentAt, CancellationToken ct = default);
    Task<int> RolloverPendingTasksAsync(string today, DateTimeOffset nowUtc, CancellationToken ct = default);
}
