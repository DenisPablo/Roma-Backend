using Dapper;
using Roma.Domain.Notifications;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Notifications;

public class DaySummaryRepository : IDaySummaryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DaySummaryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> HasSummaryBeenSentAsync(string day, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM day_summaries WHERE day = @Day LIMIT 1;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var res = await conn.ExecuteScalarAsync<int?>(sql, new { Day = day });
        return res.HasValue;
    }

    public async Task MarkSummarySentAsync(string day, string sentAt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT OR REPLACE INTO day_summaries (day, sent_at)
            VALUES (@Day, @SentAt);
            """;
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new { Day = day, SentAt = sentAt });
    }
}
