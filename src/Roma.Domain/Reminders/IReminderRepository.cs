namespace Roma.Domain.Reminders;

public interface IReminderRepository
{
    Task CreateForEventAsync(int taskId, int[] offsets, string eventDay, string eventTime, CancellationToken ct = default);
    Task ReplaceForEventAsync(int taskId, int[] offsets, string eventDay, string eventTime, CancellationToken ct = default);
    Task<IReadOnlyList<TaskReminder>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskReminder>> FindDueRemindersAsync(string nowLocal, CancellationToken ct = default);
    Task MarkSentAsync(int reminderId, string sentAt, CancellationToken ct = default);
    Task RecomputeForEventAsync(int taskId, string newDay, string newTime, string nowLocal, CancellationToken ct = default);
    Task MarkOverdueBeforeBootAsync(string bootLocal, DateTimeOffset bootUtc, CancellationToken ct = default);
}
