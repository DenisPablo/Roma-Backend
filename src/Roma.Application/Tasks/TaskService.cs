using Roma.Application.Configuration;
using Roma.Domain.Categories;
using Roma.Domain.Common;
using Roma.Domain.Exceptions;
using Roma.Domain.Tasks;

namespace Roma.Application.Tasks;

public record CreateTaskRequest(
    string Title,
    string Day,
    string ScheduledTime,
    int CategoryId,
    string? Kind = "task",
    string[]? Reminders = null);

public record UpdateTaskRequest(
    string? Title,
    string? Day,
    string? ScheduledTime,
    int? CategoryId);

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
    DateTimeOffset UpdatedAt);

public record CompleteTaskResponse(
    int Id,
    int DurationSeconds,
    string FormattedDuration);

public class TaskService
{
    private readonly ITaskRepository _taskRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly AppOptions _options;

    public TaskService(
        ITaskRepository taskRepository,
        ICategoryRepository categoryRepository,
        AppOptions options)
    {
        _taskRepository = taskRepository;
        _categoryRepository = categoryRepository;
        _options = options;
    }

    public async Task<IReadOnlyList<TaskResponse>> GetByDayAsync(string? day, CancellationToken ct = default)
    {
        var targetDay = day;
        if (string.IsNullOrWhiteSpace(targetDay))
        {
            var nowParts = TimeUtils.GetLocalParts(DateTimeOffset.UtcNow, _options.AppTz);
            targetDay = nowParts.DayString;
        }

        var list = await _taskRepository.GetByDayAsync(targetDay, ct);
        return list.Select(MapToResponse).ToList();
    }

    public async Task<TaskResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        return MapToResponse(task);
    }

    public async Task<TaskResponse> CreateAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, ct)
            ?? throw new EntityNotFoundException($"Category with ID {request.CategoryId} does not exist.");

        var kind = (request.Kind ?? "task").ToLowerInvariant();
        var entity = kind switch
        {
            "event" => TaskItem.CreateEvent(request.Title, request.Day, request.ScheduledTime, request.CategoryId),
            "task" => TaskItem.CreateTask(request.Title, request.Day, request.ScheduledTime, request.CategoryId),
            _ => throw new DomainValidationException($"Invalid task kind '{kind}'. Must be 'task' or 'event'.")
        };

        var created = await _taskRepository.AddAsync(entity, ct);
        return MapToResponse(created);
    }

    public async Task<TaskResponse> StartAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        task.Start(DateTimeOffset.UtcNow);
        await _taskRepository.UpdateAsync(task, ct);

        return MapToResponse(task);
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

        if (request.Day != null || request.ScheduledTime != null)
        {
            var newDay = request.Day ?? task.Day;
            var newTime = request.ScheduledTime ?? task.ScheduledTime;

            var nowParts = TimeUtils.GetLocalParts(DateTimeOffset.UtcNow, _options.AppTz);
            var newMoment = $"{newDay}T{newTime}";
            var currentMoment = $"{nowParts.DayString}T{nowParts.TimeString}";

            var isFuture = string.CompareOrdinal(newMoment, currentMoment) > 0;
            task.UpdateSchedule(newDay, newTime, isFuture);
        }

        await _taskRepository.UpdateAsync(task, ct);
        return MapToResponse(task);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var task = await _taskRepository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Task with ID {id} was not found.");

        await _taskRepository.DeleteAsync(id, ct);
    }

    private static TaskResponse MapToResponse(TaskItem t) =>
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
            t.UpdatedAt
        );
}
