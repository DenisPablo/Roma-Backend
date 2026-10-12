using Roma.Application.Configuration;
using Roma.Domain.Common;
using Roma.Domain.Dashboard;
using Roma.Domain.Exceptions;

namespace Roma.Application.Dashboard;

public record PeriodStats(
    string From,
    string To,
    string? Month);

public record MuscleGroupCount(
    string MuscleGroup,
    int Count);

public record WorkoutStats(
    int TotalSessions,
    int DistinctDaysCount,
    double AverageRpe,
    IReadOnlyList<string> SessionDates,
    IReadOnlyList<MuscleGroupCount> ByMuscleGroup);

public record TaskStats(
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    int InProgressTasks,
    int NotDoneTasks,
    double CompletionRate,
    int TotalDurationSeconds,
    string FormattedTotalDuration);

public record CategoryStats(
    int CategoryId,
    string CategoryName,
    int TotalTasks,
    int CompletedTasks,
    int PendingTasks,
    double CompletionRate,
    int TotalDurationSeconds,
    string FormattedTotalDuration);

public record DailyActivityStats(
    string Date,
    int TasksTotal,
    int TasksCompleted,
    int TasksPending,
    bool HadWorkout,
    int WorkoutCount);

public record DashboardStatsResponse(
    PeriodStats Period,
    WorkoutStats Workouts,
    TaskStats Tasks,
    IReadOnlyList<CategoryStats> Categories,
    IReadOnlyList<DailyActivityStats> DailyActivity);

public class DashboardService
{
    private readonly IDashboardRepository _repository;
    private readonly AppOptions _options;

    public DashboardService(IDashboardRepository repository, AppOptions options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<DashboardStatsResponse> GetStatsAsync(
        string? month = null,
        string? from = null,
        string? to = null,
        CancellationToken ct = default)
    {
        string resolvedFrom;
        string resolvedTo;

        try
        {
            var range = TimeUtils.ResolveDateRange(month, from, to, _options.AppTz);
            resolvedFrom = range.From;
            resolvedTo = range.To;
        }
        catch (ArgumentException ex)
        {
            throw new DomainValidationException(ex.Message);
        }

        var taskStatsTask = _repository.GetTaskStatsAsync(resolvedFrom, resolvedTo, ct);
        var catStatsTask = _repository.GetCategoryStatsAsync(resolvedFrom, resolvedTo, ct);
        var workoutsTask = _repository.GetWorkoutsAsync(resolvedFrom, resolvedTo, ct);
        var dailyTasksTask = _repository.GetDailyTasksAsync(resolvedFrom, resolvedTo, ct);

        await Task.WhenAll(taskStatsTask, catStatsTask, workoutsTask, dailyTasksTask);

        var rawTasks = await taskStatsTask;
        var rawCats = await catStatsTask;
        var rawWorkouts = await workoutsTask;
        var rawDailyTasks = await dailyTasksTask;

        // 1. Task Stats
        var taskCompletionRate = rawTasks.TotalTasks > 0
            ? Math.Round((double)rawTasks.CompletedTasks / rawTasks.TotalTasks * 100.0, 1)
            : 0.0;

        var taskStats = new TaskStats(
            (int)rawTasks.TotalTasks,
            (int)rawTasks.CompletedTasks,
            (int)rawTasks.PendingTasks,
            (int)rawTasks.InProgressTasks,
            (int)rawTasks.NotDoneTasks,
            taskCompletionRate,
            (int)rawTasks.TotalDurationSeconds,
            TimeUtils.FormatDuration((int)rawTasks.TotalDurationSeconds)
        );

        // 2. Category Stats
        var categoryStats = rawCats.Select(c =>
        {
            var rate = c.TotalTasks > 0
                ? Math.Round((double)c.CompletedTasks / c.TotalTasks * 100.0, 1)
                : 0.0;

            return new CategoryStats(
                (int)c.CategoryId,
                c.CategoryName,
                (int)c.TotalTasks,
                (int)c.CompletedTasks,
                (int)c.PendingTasks,
                rate,
                (int)c.TotalDurationSeconds,
                TimeUtils.FormatDuration((int)c.TotalDurationSeconds)
            );
        }).ToList();

        // 3. Workout Stats
        var distinctDays = rawWorkouts.Select(w => w.Date).Distinct().OrderBy(d => d).ToList();
        var avgRpe = rawWorkouts.Count > 0
            ? Math.Round(rawWorkouts.Average(w => (double)w.Rpe), 1)
            : 0.0;

        var byMuscleGroup = rawWorkouts
            .GroupBy(w => w.TargetMuscleGroup)
            .Select(g => new MuscleGroupCount(g.Key, g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();

        var workoutStats = new WorkoutStats(
            rawWorkouts.Count,
            distinctDays.Count,
            avgRpe,
            distinctDays,
            byMuscleGroup
        );

        // 4. Daily Activity Timeline
        var workoutsByDate = rawWorkouts.GroupBy(w => w.Date).ToDictionary(g => g.Key, g => g.Count());
        var tasksByDate = rawDailyTasks.ToDictionary(t => t.Date);

        var allDates = new SortedSet<string>(tasksByDate.Keys.Concat(workoutsByDate.Keys), StringComparer.Ordinal);
        var dailyActivity = allDates.Select(date =>
        {
            var hasTasks = tasksByDate.TryGetValue(date, out var t);
            var hasWorkouts = workoutsByDate.TryGetValue(date, out var workoutCount);

            return new DailyActivityStats(
                date,
                hasTasks ? (int)t!.TasksTotal : 0,
                hasTasks ? (int)t!.TasksCompleted : 0,
                hasTasks ? (int)t!.TasksPending : 0,
                hasWorkouts && workoutCount > 0,
                hasWorkouts ? workoutCount : 0
            );
        }).ToList();

        var period = new PeriodStats(resolvedFrom, resolvedTo, month);

        return new DashboardStatsResponse(
            period,
            workoutStats,
            taskStats,
            categoryStats,
            dailyActivity
        );
    }
}
