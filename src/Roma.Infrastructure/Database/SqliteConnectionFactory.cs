using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Roma.Infrastructure.Database;

public class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            DefaultTimeout = 30,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 10000;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -20000;
            PRAGMA temp_store = MEMORY;
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        return conn;
    }
}
