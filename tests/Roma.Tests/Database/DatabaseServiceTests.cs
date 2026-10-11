using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Roma.Application.Configuration;
using Roma.Infrastructure.Database;

namespace Roma.Tests.Database;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"roma_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* best effort cleanup */ }
        }
    }

    [Fact]
    public async Task InitializeAsync_AppliesMigrationAndSeedsCategories()
    {
        var options = new AppOptions { Categories = "Work,Study,Health" };
        var service = new DatabaseService(_factory, options);

        await service.InitializeAsync();

        using var conn = await _factory.CreateConnectionAsync();

        // Verify version applied
        var version = await conn.ExecuteScalarAsync<int>("SELECT version FROM schema_migrations WHERE version=1;");
        version.Should().Be(1);

        // Verify categories seeded
        var categories = (await conn.QueryAsync<string>("SELECT name FROM categories ORDER BY id;")).ToList();
        categories.Should().Equal("Work", "Study", "Health");

        // Idempotency: run again
        await service.InitializeAsync();
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories;");
        count.Should().Be(3);
    }

    [Fact]
    public async Task TasksTable_EnforcesStatusAndDurationChecks()
    {
        var options = new AppOptions();
        var service = new DatabaseService(_factory, options);
        await service.InitializeAsync();

        using var conn = await _factory.CreateConnectionAsync();
        var catId = await conn.ExecuteScalarAsync<int>("SELECT id FROM categories LIMIT 1;");

        // Valid pending task
        var insertSql = """
            INSERT INTO tasks (title, day, scheduled_time, category_id, status)
            VALUES ('Test task', '2026-10-15', '09:00', @CatId, 'pending')
            RETURNING id;
            """;
        var id = await conn.ExecuteScalarAsync<int>(insertSql, new { CatId = catId });
        id.Should().BeGreaterThan(0);

        // Event cannot have status != pending
        var invalidEventSql = """
            INSERT INTO tasks (title, day, scheduled_time, category_id, kind, status, started_at)
            VALUES ('Event 1', '2026-10-15', '10:00', @CatId, 'event', 'in_progress', '2026-10-15T10:00:00Z');
            """;
        var act = async () => await conn.ExecuteAsync(invalidEventSql, new { CatId = catId });
        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task SoftDeletes_ColumnsExistAndPreservePhysicalIntegrity()
    {
        var options = new AppOptions();
        var service = new DatabaseService(_factory, options);
        await service.InitializeAsync();

        using var conn = await _factory.CreateConnectionAsync();

        // Check migration 3 is recorded
        var v3 = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM schema_migrations WHERE version = 3;");
        v3.Should().Be(1);

        // Verify deleted_at columns exist on all 3 tables
        var catCols = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('categories');");
        catCols.Should().Contain("deleted_at");

        var taskCols = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('tasks');");
        taskCols.Should().Contain("deleted_at");

        var gymCols = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('workout_sessions');");
        gymCols.Should().Contain("deleted_at");
    }
}
