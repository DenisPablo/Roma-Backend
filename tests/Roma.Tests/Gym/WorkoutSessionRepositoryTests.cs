using FluentAssertions;
using Microsoft.Data.Sqlite;
using Roma.Application.Configuration;
using Roma.Domain.Gym;
using Roma.Infrastructure.Database;
using Roma.Infrastructure.Gym;

namespace Roma.Tests.Gym;

public class WorkoutSessionRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseService _dbService;
    private readonly WorkoutSessionRepository _repo;

    public WorkoutSessionRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"roma_gym_repo_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        var options = new AppOptions();
        _dbService = new DatabaseService(_factory, options);
        _repo = new WorkoutSessionRepository(_factory);
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
    public async Task Repository_CrudAndRangeQuery_WorksAsExpected()
    {
        await _dbService.InitializeAsync();

        // 1. Add session
        var session1 = WorkoutSession.Create("2026-10-10", "Legs", 8, 4, 3, "Squat day");
        var created1 = await _repo.AddAsync(session1);
        created1.Id.Should().BeGreaterThan(0);

        var session2 = WorkoutSession.Create("2026-10-12", "Chest", 7, 5, 4, "Bench day");
        var created2 = await _repo.AddAsync(session2);

        var session3 = WorkoutSession.Create("2026-10-15", "Back", 9, 3, 2, "Deadlift day");
        var created3 = await _repo.AddAsync(session3);

        // 2. Lookup by ID
        var fetched = await _repo.GetByIdAsync(created2.Id);
        fetched.Should().NotBeNull();
        fetched!.TargetMuscleGroup.Should().Be("Chest");
        fetched.Rpe.Should().Be(7);

        // 3. Range query: from 2026-10-11 to 2026-10-14 should only return Chest (session 2)
        var filtered = await _repo.GetSessionsAsync("2026-10-11", "2026-10-14");
        filtered.Should().HaveCount(1);
        filtered[0].Id.Should().Be(created2.Id);

        // 4. Query all (no bounds): ordered by date DESC
        var all = await _repo.GetSessionsAsync(null, null);
        all.Should().HaveCount(3);
        all.Select(s => s.Date).Should().Equal("2026-10-15", "2026-10-12", "2026-10-10");

        // 5. Delete
        var deleted = await _repo.DeleteAsync(created1.Id);
        deleted.Should().BeTrue();

        var recheck = await _repo.GetByIdAsync(created1.Id);
        recheck.Should().BeNull();
    }
}
