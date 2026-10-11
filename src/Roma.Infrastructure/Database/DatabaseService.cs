using System.Data.Common;
using Dapper;
using Roma.Application.Configuration;

namespace Roma.Infrastructure.Database;

public class DatabaseService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly AppOptions _options;

    public DatabaseService(IDbConnectionFactory connectionFactory, AppOptions options)
    {
        _connectionFactory = connectionFactory;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);

        // Run migrations
        await ApplyMigrationsAsync(conn, ct);

        // Seed categories if table is empty
        await SeedCategoriesAsync(conn, ct);
    }

    private static async Task ApplyMigrationsAsync(DbConnection conn, CancellationToken ct)
    {
        // Ensure schema_migrations table exists
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
              version INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            );
            """);

        var appliedVersions = (await conn.QueryAsync<int>("SELECT version FROM schema_migrations;")).ToHashSet();

        // Migration 1: 001_init.sql
        if (!appliedVersions.Contains(1))
        {
            using var tx = await conn.BeginTransactionAsync(ct);
            const string sql = """
                CREATE TABLE IF NOT EXISTS categories (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                );

                CREATE TABLE IF NOT EXISTS tasks (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  title TEXT NOT NULL,
                  day TEXT NOT NULL,
                  scheduled_time TEXT NOT NULL,
                  category_id INTEGER NOT NULL REFERENCES categories(id),
                  kind TEXT NOT NULL DEFAULT 'task' CHECK (kind IN ('task','event')),
                  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','in_progress','completed','not_done')),
                  started_at TEXT,
                  completed_at TEXT,
                  duration_seconds INTEGER,
                  day_closed_at TEXT,
                  reminder_sent_at TEXT,
                  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                  CHECK ((status IN ('in_progress','completed')) = (started_at IS NOT NULL)),
                  CHECK ((status = 'completed') = (completed_at IS NOT NULL)),
                  CHECK ((status = 'not_done') = (day_closed_at IS NOT NULL)),
                  CHECK (status != 'completed' OR (duration_seconds IS NOT NULL AND completed_at >= started_at)),
                  CHECK (kind != 'event' OR status = 'pending')
                );

                CREATE INDEX IF NOT EXISTS idx_tasks_day ON tasks(day, status, scheduled_time);
                CREATE INDEX IF NOT EXISTS idx_tasks_category ON tasks(category_id);
                CREATE INDEX IF NOT EXISTS idx_tasks_due ON tasks(day, scheduled_time)
                  WHERE kind='task' AND reminder_sent_at IS NULL AND status IN ('pending','in_progress');

                CREATE TABLE IF NOT EXISTS task_reminders (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                  offset_minutes INTEGER NOT NULL,
                  due_at TEXT NOT NULL,
                  sent_at TEXT,
                  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                  UNIQUE (task_id, offset_minutes)
                );

                CREATE INDEX IF NOT EXISTS idx_task_reminders_due ON task_reminders(due_at) WHERE sent_at IS NULL;

                CREATE TABLE IF NOT EXISTS day_summaries (
                  day TEXT PRIMARY KEY,
                  sent_at TEXT NOT NULL
                );

                INSERT INTO schema_migrations (version, name) VALUES (1, '001_init.sql');
                """;

            await conn.ExecuteAsync(sql, transaction: tx);
            await tx.CommitAsync(ct);
        }

        // Migration 2: 002_gym_tracking.sql
        if (!appliedVersions.Contains(2))
        {
            using var tx = await conn.BeginTransactionAsync(ct);
            const string sql = """
                CREATE TABLE IF NOT EXISTS workout_sessions (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  date TEXT NOT NULL,
                  target_muscle_group TEXT NOT NULL,
                  rpe INTEGER NOT NULL CHECK (rpe BETWEEN 1 AND 10),
                  feeling INTEGER NOT NULL CHECK (feeling BETWEEN 1 AND 5),
                  recovery_score INTEGER NOT NULL CHECK (recovery_score BETWEEN 1 AND 5),
                  notes TEXT,
                  created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                  updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
                );

                CREATE INDEX IF NOT EXISTS idx_workout_sessions_date ON workout_sessions(date DESC);
                CREATE INDEX IF NOT EXISTS idx_workout_sessions_muscle ON workout_sessions(target_muscle_group);

                INSERT INTO schema_migrations (version, name) VALUES (2, '002_gym_tracking.sql');
                """;

            await conn.ExecuteAsync(sql, transaction: tx);
            await tx.CommitAsync(ct);
        }
    }

    private async Task SeedCategoriesAsync(DbConnection conn, CancellationToken ct)
    {
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories;");
        if (count > 0) return;

        var raw = _options.Categories;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "Trabajo,Estudio,Salud,Personal,Hogar,Otros";
        }

        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Distinct(StringComparer.OrdinalIgnoreCase);

        using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var cat in list)
        {
            await conn.ExecuteAsync(
                "INSERT OR IGNORE INTO categories (name) VALUES (@Name);",
                new { Name = cat },
                transaction: tx);
        }
        await tx.CommitAsync(ct);
    }
}
