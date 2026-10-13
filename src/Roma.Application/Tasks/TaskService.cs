using Roma.Application.Configuration;
using Roma.Domain.Categories;
using Roma.Domain.Common;
using Roma.Domain.Exceptions;
using Roma.Domain.Reminders;
using Roma.Domain.Tasks;

namespace Roma.Application.Tasks;

public record TaskReminderResponse(
    int Id,
    int OffsetMinutes,
    string DueAt,
    string? SentAt);

public record CreateTaskRequest(
    string Title,
    string Day,
    string ScheduledTime,
    int CategoryId,
    string? Kind = "task",
    string[]? Reminders = null,
    int? DurationSeconds = null,
    bool? IsCompleted = false);

public record UpdateTaskRequest(
    string? Title,
    string? Day,
    string? ScheduledTime,
    int? CategoryId,
    int? DurationSeconds = null);

public record TaskResponse(
    int Id,
    string Title,
    string Day,
    string ScheduledTime,
    int CategoryId,
    string Kind,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? DurationSeconds,
    string? FormattedDuration,
    DateTimeOffset? DayClosedAt,
    string? ReminderSentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaskReminderResponse>? Reminders = null);

public record CompleteTaskResponse(
    int Id,
    int DurationSeconds,
    string FormattedDuration);

public class TaskService
{
    private readonly ITaskRepository _taskRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IReminderRepository _reminderRepository;
    private readonly AppOptions _options;

    public TaskService(
        ITaskRepository taskRepository,
        ICategoryRepository categoryRepository,
        IReminderRepository reminderRepository,
        AppOptions options)
    {
        _taskRepository = taskRepository;
        _categoryRepository = categoryRepository;
        _reminderRepository = reminderRepository;
        _options = options;
    }

    public async Task<IReadOnlyList<TaskResponse>> GetTasksAsync(
        string? date = null,
        string? from = null,
        string? to = null,
        string? month = null,
        int? categoryId = null,
        string? status = null,
        string? kind = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(month))
        {
            try
            {
                var range = TimeUtils.ResolveDateRange(month, null, null, _options.AppTz);
                from = range.From;
                to = range.To;
            }
            catch (ArgumentException ex)
            {
                throw new DomainValidationException(ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to) && string.CompareOrdinal(from, to) > 0)
        {
            throw new DomainValidationException("'from' date cannot be after 'to' date.");
        }

        var list = await _taskRepository.GetTasksAsync(from, to, date, categoryId, status, kind, ct);
        var results = new List<TaskResponse>(list.Count);
        foreach (var task in list)
        {
            var reminders = task.Kind == "event"
                ? await GetRemindersAsync(task.Id, ct)
                : null;
            results.Add(MapToResponse(task, reminders));
        }

        return results;
    }

    public async Task<IReadOnlyList<TaskResponse>> GetByDayAsync(string? day, CancellationToken ct = default)
    {
        return await GetTasksAsync(date: day, ct: ct);
    }

    public async Task<TaskResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        var reminders = task.Kind == "event"
            ? await GetRemindersAsync(task.Id, ct)
            : null;

        return MapToResponse(task, reminders);
    }

    public async Task<TaskResponse> CreateAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, ct)
            ?? throw new EntityNotFoundException($"Category with ID {request.CategoryId} does not exist.");

        var kind = (request.Kind ?? "task").ToLowerInvariant();
        var entity = kind switch
        {
            "event" => TaskItem.CreateEvent(request.Title, request.Day, request.ScheduledTime, request.CategoryId),
            "task" => TaskItem.CreateTask(
                request.Title,
                request.Day,
                request.ScheduledTime,
                request.CategoryId,
                request.DurationSeconds,
                request.IsCompleted ?? false),
            _ => throw new DomainValidationException($"Invalid task kind '{kind}'. Must be 'task' or 'event'.")
        };

        var created = await _taskRepository.AddAsync(entity, ct);

        IReadOnlyList<TaskReminderResponse>? reminders = null;
        if (created.Kind == "event")
        {
            int[] offsets;
            if (request.Reminders != null && request.Reminders.Length > 0)
            {
                var spec = string.Join(",", request.Reminders);
                offsets = TimeUtils.ParseOffsets(spec);
            }
            else
            {
                offsets = TimeUtils.ParseOffsets(_options.EventReminderOffsets);
            }

            await _reminderRepository.CreateForEventAsync(created.Id, offsets, created.Day, created.ScheduledTime, ct);
            reminders = await GetRemindersAsync(created.Id, ct);
        }

        return MapToResponse(created, reminders);
    }

    public async Task<TaskResponse> StartAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        task.Start(DateTimeOffset.UtcNow);
        await _taskRepository.UpdateAsync(task, ct);

        var reminders = task.Kind == "event" ? await GetRemindersAsync(task.Id, ct) : null;
        return MapToResponse(task, reminders);
    }

    public async Task<CompleteTaskResponse> CompleteAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        task.Complete(DateTimeOffset.UtcNow);
        await _taskRepository.UpdateAsync(task, ct);

        var duration = task.DurationSeconds ?? 0;
        return new CompleteTaskResponse(task.Id, duration, TimeUtils.FormatDuration(duration));
    }

    public async Task<TaskResponse> UpdateAsync(int id, UpdateTaskRequest request, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            task.UpdateTitle(request.Title);
        }

        if (request.CategoryId.HasValue && request.CategoryId.Value != task.CategoryId)
        {
            var category = await _categoryRepository.GetByIdAsync(request.CategoryId.Value, ct)
                ?? throw new EntityNotFoundException($"Category with ID {request.CategoryId.Value} does not exist.");
            task.UpdateCategory(category.Id);
        }

        if (request.Day != null || request.ScheduledTime != null)
        {
            var newDay = request.Day ?? task.Day;
            var newTime = request.ScheduledTime ?? task.ScheduledTime;

            if (newDay != task.Day || newTime != task.ScheduledTime)
            {
                var nowParts = TimeUtils.GetLocalParts(DateTimeOffset.UtcNow, _options.AppTz);
                var newMoment = $"{newDay}T{newTime}";
                var currentMoment = $"{nowParts.DayString}T{nowParts.TimeString}";

                var isFuture = string.CompareOrdinal(newMoment, currentMoment) > 0;
                task.UpdateSchedule(newDay, newTime, isFuture);

                if (task.Kind == "event")
                {
                    await _reminderRepository.RecomputeForEventAsync(task.Id, newDay, newTime, currentMoment, ct);
                }
            }
        }

        if (request.DurationSeconds.HasValue)
        {
            task.UpdateDuration(request.DurationSeconds.Value);
        }

        await _taskRepository.UpdateAsync(task, ct);

        var reminders = task.Kind == "event" ? await GetRemindersAsync(task.Id, ct) : null;
        return MapToResponse(task, reminders);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        await _taskRepository.DeleteAsync(id, ct);
    }

    private async Task<IReadOnlyList<TaskReminderResponse>> GetRemindersAsync(int taskId, CancellationToken ct)
    {
        var list = await _reminderRepository.GetByTaskIdAsync(taskId, ct);
        return list.Select(r => new TaskReminderResponse(r.Id, r.OffsetMinutes, r.DueAt, r.SentAt)).ToList();
    }

    private static TaskResponse MapToResponse(TaskItem t, IReadOnlyList<TaskReminderResponse>? reminders = null) =>
        new(
            t.Id,
            t.Title,
            t.Day,
            t.ScheduledTime,
            t.CategoryId,
            t.Kind,
            t.Status,
            t.StartedAt,
            t.CompletedAt,
            t.DurationSeconds,
            t.DurationSeconds.HasValue ? TimeUtils.FormatDuration(t.DurationSeconds.Value) : null,
            t.DayClosedAt,
            t.ReminderSentAt,
            t.CreatedAt,
            t.UpdatedAt,
            reminders
        );
}
