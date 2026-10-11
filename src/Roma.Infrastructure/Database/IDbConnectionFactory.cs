using System.Data.Common;

namespace Roma.Infrastructure.Database;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default);
}
