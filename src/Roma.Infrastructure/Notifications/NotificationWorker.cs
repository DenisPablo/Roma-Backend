using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Roma.Application.Configuration;
using Roma.Application.Notifications;
using Roma.Domain.Common;
using Roma.Domain.Notifications;
using Roma.Domain.Reminders;
using Roma.Domain.Tasks;

namespace Roma.Infrastructure.Notifications;

public class NotificationWorker : BackgroundService
{
    private readonly ITaskRepository _taskRepository;
    private readonly IReminderRepository _reminderRepository;
    private readonly IDaySummaryRepository _daySummaryRepository;
    private readonly INotifierPort _notifier;
    private readonly AppOptions _options;
    private readonly ILogger<NotificationWorker> _logger;

    public DateTimeOffset BootUtc { get; private set; }
    public string BootLocal { get; private set; } = string.Empty;

    public NotificationWorker(
        ITaskRepository taskRepository,
        IReminderRepository reminderRepository,
        IDaySummaryRepository daySummaryRepository,
        INotifierPort notifier,
        AppOptions options,
        ILogger<NotificationWorker> logger)
    {
        _taskRepository = taskRepository;
        _reminderRepository = reminderRepository;
        _daySummaryRepository = daySummaryRepository;
        _notifier = notifier;
        _options = options;
        _logger = logger;
    }

    public async Task RunBootSweepAsync(CancellationToken ct = default)
    {
        BootUtc = DateTimeOffset.UtcNow;
        var bootParts = TimeUtils.GetLocalParts(BootUtc, _options.AppTz);
        BootLocal = bootParts.IsoString;

        _logger.LogInformation("Running boot sweep for BootUtc: {BootUtc}, BootLocal: {BootLocal}", BootUtc, BootLocal);
        await _reminderRepository.MarkOverdueBeforeBootAsync(BootLocal, BootUtc, ct);
    }

    public async Task ProcessTickAsync(DateTimeOffset utcNow, CancellationToken ct = default)
    {
        var localParts = TimeUtils.GetLocalParts(utcNow, _options.AppTz);
        var today = localParts.DayString;
        var currentTime = localParts.TimeString;
        var currentIso = localParts.IsoString;

        // 1. Rollover sweep: tasks pending from past days become not_done
        try
        {
            var rolledOver = await _taskRepository.RolloverPendingTasksAsync(today, utcNow, ct);
            if (rolledOver > 0)
            {
                _logger.LogInformation("Rolled over {Count} overdue tasks to not_done.", rolledOver);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollover sweep.");
        }

        // 2. Find due tasks (kind='task')
        try
        {
            var dueTasks = await _taskRepository.FindDueTasksAsync(today, currentTime, ct);
            foreach (var task in dueTasks)
            {
                var msg = $"⏰ Tarea programada: {task.Title} (Hora: {task.ScheduledTime})";
                await _notifier.SendAsync(msg, ct);
                await _taskRepository.MarkReminderSentAsync(task.Id, currentIso, ct);
                _logger.LogInformation("Notification sent for task {TaskId} - {Title}", task.Id, task.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking due tasks.");
        }

        // 3. Find due reminders (kind='event', P2 policy)
        try
        {
            var dueReminders = await _reminderRepository.FindDueRemindersAsync(currentIso, ct);
            foreach (var rem in dueReminders)
            {
                var task = await _taskRepository.GetByIdAsync(rem.TaskId, ct);
                if (task != null)
                {
                    var msg = $"🔔 Recordatorio de evento: {task.Title} (Fecha: {task.Day} a las {task.ScheduledTime})";
                    await _notifier.SendAsync(msg, ct);
                }
                await _reminderRepository.MarkSentAsync(rem.Id, currentIso, ct);
                _logger.LogInformation("Notification sent for event reminder {ReminderId} (Task {TaskId})", rem.Id, rem.TaskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking due event reminders.");
        }

        // 4. EOD Summary check
        try
        {
            if (string.CompareOrdinal(currentTime, _options.EodTime) >= 0)
            {
                var alreadySent = await _daySummaryRepository.HasSummaryBeenSentAsync(today, ct);
                if (!alreadySent)
                {
                    await SendEodSummaryAsync(today, currentIso, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing EOD summary.");
        }
    }

    private async Task SendEodSummaryAsync(string today, string currentIso, CancellationToken ct)
    {
        var allTasks = await _taskRepository.GetByDayAsync(today, ct);
        var tasksOnly = allTasks.Where(t => t.Kind == "task").ToList();

        var completed = tasksOnly.Where(t => t.Status == "completed").ToList();
        var inProgress = tasksOnly.Where(t => t.Status == "in_progress").ToList();
        var pending = tasksOnly.Where(t => t.Status == "pending").ToList();
        var notDone = tasksOnly.Where(t => t.Status == "not_done").ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"📋 Resumen del día — {today}");
        sb.AppendLine();

        if (completed.Count > 0)
        {
            sb.AppendLine($"✅ Completadas ({completed.Count}):");
            foreach (var t in completed)
            {
                var dur = t.DurationSeconds.HasValue ? TimeUtils.FormatDuration(t.DurationSeconds.Value) : "0m";
                sb.AppendLine($"  • {t.Title} ({dur})");
            }
        }
        else
        {
            sb.AppendLine("✅ Completadas: 0");
        }

        if (inProgress.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"⏳ En curso ({inProgress.Count}):");
            foreach (var t in inProgress)
            {
                sb.AppendLine($"  • {t.Title}");
            }
        }

        if (notDone.Count > 0 || pending.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"❌ No completadas: {notDone.Count + pending.Count}");
        }

        await _notifier.SendAsync(sb.ToString().TrimEnd(), ct);
        await _daySummaryRepository.MarkSummarySentAsync(today, currentIso, ct);
        _logger.LogInformation("EOD summary sent for {Day}.", today);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunBootSweepAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessTickAsync(DateTimeOffset.UtcNow, stoppingToken);
        }
    }
}
