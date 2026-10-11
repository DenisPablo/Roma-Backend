namespace Roma.Domain.Reminders;

public class TaskReminder
{
    public int Id { get; private set; }
    public int TaskId { get; private set; }
    public int OffsetMinutes { get; private set; }
    public string DueAt { get; private set; } = string.Empty;
    public string? SentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TaskReminder() { }

    public static TaskReminder Create(int taskId, int offsetMinutes, string dueAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskReminder
        {
            TaskId = taskId,
            OffsetMinutes = offsetMinutes,
            DueAt = dueAt,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static TaskReminder Reconstitute(
        int id,
        int taskId,
        int offsetMinutes,
        string dueAt,
        string? sentAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new TaskReminder
        {
            Id = id,
            TaskId = taskId,
            OffsetMinutes = offsetMinutes,
            DueAt = dueAt,
            SentAt = sentAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void MarkSent(string sentAt)
    {
        SentAt = sentAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
