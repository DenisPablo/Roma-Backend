using System.Globalization;
using System.Text.RegularExpressions;
using Roma.Domain.Exceptions;

namespace Roma.Domain.Tasks;

public class TaskItem
{
    private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"^\d{2}:\d{2}$", RegexOptions.Compiled);

    public int Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Day { get; private set; } = string.Empty;
    public string ScheduledTime { get; private set; } = string.Empty;
    public int CategoryId { get; private set; }
    public string Kind { get; private set; } = "task";
    public string Status { get; private set; } = "pending";
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int? DurationSeconds { get; private set; }
    public DateTimeOffset? DayClosedAt { get; private set; }
    public string? ReminderSentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TaskItem() { }

    public static TaskItem CreateTask(
        string title,
        string day,
        string scheduledTime,
        int categoryId,
        int? durationSeconds = null,
        bool isCompleted = false)
    {
        ValidateCommon(title, day, scheduledTime, categoryId);

        if (durationSeconds.HasValue && durationSeconds.Value < 0)
            throw new DomainValidationException("Duration cannot be negative.");

        var now = DateTimeOffset.UtcNow;
        if (isCompleted)
        {
            var dur = durationSeconds ?? 0;
            return new TaskItem
            {
                Title = title.Trim(),
                Day = day.Trim(),
                ScheduledTime = scheduledTime.Trim(),
                CategoryId = categoryId,
                Kind = "task",
                Status = "completed",
                StartedAt = now.AddSeconds(-dur),
                CompletedAt = now,
                DurationSeconds = dur,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        return new TaskItem
        {
            Title = title.Trim(),
            Day = day.Trim(),
            ScheduledTime = scheduledTime.Trim(),
            CategoryId = categoryId,
            Kind = "task",
            Status = "pending",
            DurationSeconds = durationSeconds,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static TaskItem CreateEvent(string title, string day, string scheduledTime, int categoryId)
    {
        ValidateCommon(title, day, scheduledTime, categoryId);

        var now = DateTimeOffset.UtcNow;
        return new TaskItem
        {
            Title = title.Trim(),
            Day = day.Trim(),
            ScheduledTime = scheduledTime.Trim(),
            CategoryId = categoryId,
            Kind = "event",
            Status = "pending",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static TaskItem Reconstitute(
        int id,
        string title,
        string day,
        string scheduledTime,
        int categoryId,
        string kind,
        string status,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        int? durationSeconds,
        DateTimeOffset? dayClosedAt,
        string? reminderSentAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new TaskItem
        {
            Id = id,
            Title = title,
            Day = day,
            ScheduledTime = scheduledTime,
            CategoryId = categoryId,
            Kind = kind,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationSeconds = durationSeconds,
            DayClosedAt = dayClosedAt,
            ReminderSentAt = reminderSentAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Start(DateTimeOffset nowUtc)
    {
        if (Kind == "event")
            throw new DomainConflictException("Events cannot be transitioned to in_progress.");

        if (Status == "in_progress")
            return; // Idempotent no-op

        if (Status != "pending")
            throw new DomainConflictException($"Cannot start a task with status '{Status}'. Only pending tasks can be started.");

        Status = "in_progress";
        StartedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        if (Kind == "event")
            throw new DomainConflictException("Events cannot be completed.");

        if (Status != "in_progress")
            throw new DomainConflictException($"Cannot complete a task with status '{Status}'. Only in_progress tasks can be completed.");

        if (!StartedAt.HasValue)
            throw new DomainConflictException("Task has no started_at timestamp.");

        Status = "completed";
        CompletedAt = nowUtc;
        DurationSeconds = Math.Max(0, (int)(nowUtc - StartedAt.Value).TotalSeconds);
        UpdatedAt = nowUtc;
    }

    public void CloseDay(DateTimeOffset nowUtc)
    {
        if (Kind == "event")
            return; // Events are not closed on day rollover

        if (Status == "pending")
        {
            Status = "not_done";
            DayClosedAt = nowUtc;
            UpdatedAt = nowUtc;
        }
    }

    public void UpdateTitle(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            throw new DomainValidationException("Title cannot be empty.");

        Title = newTitle.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCategory(int newCategoryId)
    {
        if (newCategoryId <= 0)
            throw new DomainValidationException("A valid CategoryId must be specified.");

        CategoryId = newCategoryId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDuration(int durationSeconds)
    {
        if (durationSeconds < 0)
            throw new DomainValidationException("Duration cannot be negative.");

        DurationSeconds = durationSeconds;
        if (Status == "completed" && CompletedAt.HasValue)
        {
            StartedAt = CompletedAt.Value.AddSeconds(-durationSeconds);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateSchedule(string newDay, string newTime, bool isFutureInTz)
    {
        if (Status != "pending")
            throw new DomainConflictException($"Cannot change day or scheduled time for a task with status '{Status}'. Only pending tasks allow schedule edits.");

        ValidateDay(newDay);
        ValidateTime(newTime);

        Day = newDay.Trim();
        ScheduledTime = newTime.Trim();

        if (isFutureInTz)
        {
            ReminderSentAt = null; // Re-notification reset
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReminderSent(string sentAtMoment)
    {
        ReminderSentAt = sentAtMoment;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateCommon(string title, string day, string scheduledTime, int categoryId)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainValidationException("Task title cannot be empty.");

        if (categoryId <= 0)
            throw new DomainValidationException("A valid CategoryId must be specified.");

        ValidateDay(day);
        ValidateTime(scheduledTime);
    }

    private static void ValidateDay(string day)
    {
        if (string.IsNullOrWhiteSpace(day) || !DateRegex.IsMatch(day.Trim()))
            throw new DomainValidationException("Day must be in format YYYY-MM-DD.");

        if (!DateTime.TryParseExact(day.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new DomainValidationException("Day is not a valid calendar date.");
    }

    private static void ValidateTime(string scheduledTime)
    {
        if (string.IsNullOrWhiteSpace(scheduledTime) || !TimeRegex.IsMatch(scheduledTime.Trim()))
            throw new DomainValidationException("Scheduled time must be in format HH:MM.");

        if (!TimeSpan.TryParseExact(scheduledTime.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var ts) || ts.TotalHours >= 24)
            throw new DomainValidationException("Scheduled time is not a valid 24-hour time.");
    }
}
