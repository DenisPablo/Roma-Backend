using FluentAssertions;
using Microsoft.Data.Sqlite;
using Roma.Application.Configuration;
using Roma.Domain.Categories;
using Roma.Domain.Reminders;
using Roma.Domain.Tasks;
using Roma.Infrastructure.Categories;
using Roma.Infrastructure.Database;
using Roma.Infrastructure.Reminders;
using Roma.Infrastructure.Tasks;

namespace Roma.Tests.Reminders;

public class ReminderRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseService _dbService;
    private readonly TaskRepository _taskRepo;
    private readonly CategoryRepository _categoryRepo;
    private readonly ReminderRepository _reminderRepo;

    public ReminderRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"roma_rem_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        var options = new AppOptions { Categories = "General" };
        _dbService = new DatabaseService(_factory, options);
        _taskRepo = new TaskRepository(_factory);
        _categoryRepo = new CategoryRepository(_factory);
        _reminderRepo = new ReminderRepository(_factory);
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
    public async Task CreateForEventAsync_PersistsCalculatedDueDates()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        var evt = TaskItem.CreateEvent("Doctor", "2026-10-20", "15:00", cat.Id);
        var created = await _taskRepo.AddAsync(evt);

        var offsets = new[] { -1440, -120, 0 }; // 1 day before, 2 hours before, at time
        await _reminderRepo.CreateForEventAsync(created.Id, offsets, created.Day, created.ScheduledTime);

        var reminders = await _reminderRepo.GetByTaskIdAsync(created.Id);
        reminders.Should().HaveCount(3);
        reminders.Select(r => r.OffsetMinutes).Should().Equal(-1440, -120, 0);
        reminders.Select(r => r.DueAt).Should().Equal("2026-10-19T15:00", "2026-10-20T13:00", "2026-10-20T15:00");
    }

    [Fact]
    public async Task FindDueRemindersAsync_AppliesP2Policy_ReturnsMaxDueAtOnly()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        var evt = TaskItem.CreateEvent("Final Exam", "2026-10-20", "10:00", cat.Id);
        var created = await _taskRepo.AddAsync(evt);

        // Offsets: 7d before (13th), 3d before (17th), 1d before (19th), 2h before (20th 08:00), 0 (20th 10:00)
        var offsets = new[] { -10080, -4320, -1440, -120, 0 };
        await _reminderRepo.CreateForEventAsync(created.Id, offsets, created.Day, created.ScheduledTime);

        // Current time is 2026-10-18T12:00:
        // -10080 is due (2026-10-13T10:00)
        // -4320 is due (2026-10-17T10:00)
        // Others are in the future
        var dueList = await _reminderRepo.FindDueRemindersAsync("2026-10-18T12:00");

        // P2: Exactly ONE reminder returned per event, which is the MAX(due_at) -> -4320 (2026-10-17T10:00)
        dueList.Should().HaveCount(1);
        dueList[0].TaskId.Should().Be(created.Id);
        dueList[0].OffsetMinutes.Should().Be(-4320);
        dueList[0].DueAt.Should().Be("2026-10-17T10:00");
    }

    [Fact]
    public async Task RecomputeForEventAsync_ClearsSentAtOnlyForFutureReminders()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        var evt = TaskItem.CreateEvent("Conference", "2026-10-20", "10:00", cat.Id);
        var created = await _taskRepo.AddAsync(evt);

        var offsets = new[] { -1440, 0 }; // 19th 10:00, 20th 10:00
        await _reminderRepo.CreateForEventAsync(created.Id, offsets, created.Day, created.ScheduledTime);

        var initial = await _reminderRepo.GetByTaskIdAsync(created.Id);
        await _reminderRepo.MarkSentAsync(initial[0].Id, "2026-10-19T10:05");
        await _reminderRepo.MarkSentAsync(initial[1].Id, "2026-10-20T10:05");

        // Now move the conference to 2026-10-25T10:00. Current time is 2026-10-21T00:00
        // New due dates will be: 24th 10:00 and 25th 10:00 (both in future relative to 2026-10-21T00:00)
        await _reminderRepo.RecomputeForEventAsync(created.Id, "2026-10-25", "10:00", "2026-10-21T00:00");

        var recomputed = await _reminderRepo.GetByTaskIdAsync(created.Id);
        recomputed[0].DueAt.Should().Be("2026-10-24T10:00");
        recomputed[0].SentAt.Should().BeNull(); // Cleared because it is in future

        recomputed[1].DueAt.Should().Be("2026-10-25T10:00");
        recomputed[1].SentAt.Should().BeNull(); // Cleared because it is in future
    }

    [Fact]
    public async Task MarkOverdueBeforeBootAsync_MarksPreBootWithoutSending()
    {
        await _dbService.InitializeAsync();
        var cat = (await _categoryRepo.GetAllAsync())[0];

        var evt = TaskItem.CreateEvent("Old Event", "2026-10-10", "10:00", cat.Id);
        var created = await _taskRepo.AddAsync(evt);

        await _reminderRepo.CreateForEventAsync(created.Id, new[] { 0 }, "2026-10-10", "10:00");

        var bootLocal = "2026-10-15T08:00";
        var bootUtc = DateTimeOffset.UtcNow;

        await _reminderRepo.MarkOverdueBeforeBootAsync(bootLocal, bootUtc);

        var list = await _reminderRepo.GetByTaskIdAsync(created.Id);
        list[0].SentAt.Should().Be(bootLocal); // Marked sent as part of boot sweep
    }
}
