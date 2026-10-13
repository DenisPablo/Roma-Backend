using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Roma.Application.Configuration;
using Roma.Application.Notifications;
using Roma.Domain.Categories;
using Roma.Domain.Common;
using Roma.Domain.Notifications;
using Roma.Domain.Reminders;
using Roma.Domain.Tasks;
using Roma.Infrastructure.Categories;
using Roma.Infrastructure.Database;
using Roma.Infrastructure.Notifications;
using Roma.Infrastructure.Reminders;
using Roma.Infrastructure.Tasks;

namespace Roma.Tests.Notifications;

public class NotificationWorkerTests : IDisposable
{
    private class TestNotifier : INotifierPort
    {
        public List<string> SentMessages { get; } = new();

        public Task SendAsync(string text, CancellationToken ct = default)
        {
            SentMessages.Add(text);
            return Task.CompletedTask;
        }
    }

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseService _dbService;
    private readonly TaskRepository _taskRepo;
    private readonly CategoryRepository _categoryRepo;
    private readonly ReminderRepository _reminderRepo;
    private readonly DaySummaryRepository _daySummaryRepo;
    private readonly TestNotifier _notifier;
    private readonly AppOptions _options;
    private readonly NotificationWorker _worker;

    public NotificationWorkerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"roma_notif_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        _options = new AppOptions
        {
            Categories = "Work",
            AppTz = "UTC",
            EodTime = "23:55"
        };
        _dbService = new DatabaseService(_factory, _options);
        _taskRepo = new TaskRepository(_factory);
        _categoryRepo = new CategoryRepository(_factory);
        _reminderRepo = new ReminderRepository(_factory);
        _daySummaryRepo = new DaySummaryRepository(_factory);
        _notifier = new TestNotifier();

        var services = new ServiceCollection();
        services.AddScoped<ITaskRepository>(_ => _taskRepo);
        services.AddScoped<IReminderRepository>(_ => _reminderRepo);
        services.AddScoped<IDaySummaryRepository>(_ => _daySummaryRepo);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        _worker = new NotificationWorker(
            scopeFactory,
            _notifier,
            _options,
            NullLogger<NotificationWorker>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ProcessTickAsync_DueTask_SendsNotificationAndMarksSent()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        // Create task due at 14:00 on 2026-10-15
        var task = TaskItem.CreateTask("Review Code", "2026-10-15", "14:00", cat.Id);
        var created = await _taskRepo.AddAsync(task);

        // Tick before due time (13:59)
        var preTime = new DateTimeOffset(2026, 10, 15, 13, 59, 0, TimeSpan.Zero);
        await _worker.ProcessTickAsync(preTime);
        _notifier.SentMessages.Should().BeEmpty();

        // Tick at due time (14:00)
        var dueTime = new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
        await _worker.ProcessTickAsync(dueTime);

        _notifier.SentMessages.Should().HaveCount(1);
        _notifier.SentMessages[0].Should().Contain("Review Code");

        // Verify marked sent
        var reloaded = await _taskRepo.GetByIdAsync(created.Id);
        reloaded!.ReminderSentAt.Should().Be("2026-10-15T14:00");

        // Subsequent tick does not duplicate message
        await _worker.ProcessTickAsync(dueTime.AddMinutes(1));
        _notifier.SentMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessTickAsync_Rollover_MarksPastPendingAsNotDone()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        // Task created for yesterday
        var yesterdayTask = TaskItem.CreateTask("Old Pending Task", "2026-10-14", "10:00", cat.Id);
        var created = await _taskRepo.AddAsync(yesterdayTask);

        // Tick on 2026-10-15
        var today = new DateTimeOffset(2026, 10, 15, 08, 0, 0, TimeSpan.Zero);
        await _worker.ProcessTickAsync(today);

        var reloaded = await _taskRepo.GetByIdAsync(created.Id);
        reloaded!.Status.Should().Be("not_done");
        reloaded.DayClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessTickAsync_EodSummary_SendsSummaryAfterEodTime()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        // Completed task
        var t1 = TaskItem.CreateTask("Task Completed", "2026-10-15", "10:00", cat.Id);
        var c1 = await _taskRepo.AddAsync(t1);
        c1.Start(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
        c1.Complete(new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.Zero));
        await _taskRepo.UpdateAsync(c1);

        // Tick at 23:55 (EodTime)
        var eodTime = new DateTimeOffset(2026, 10, 15, 23, 55, 0, TimeSpan.Zero);
        await _worker.ProcessTickAsync(eodTime);

        _notifier.SentMessages.Should().Contain(m => m.Contains("Resumen del día") && m.Contains("Task Completed"));

        // Day summary marked sent
        var hasSent = await _daySummaryRepo.HasSummaryBeenSentAsync("2026-10-15");
        hasSent.Should().BeTrue();

        // Repeating tick at 23:56 does not duplicate summary
        _notifier.SentMessages.Clear();
        await _worker.ProcessTickAsync(eodTime.AddMinutes(1));
        _notifier.SentMessages.Should().BeEmpty();
    }
}
