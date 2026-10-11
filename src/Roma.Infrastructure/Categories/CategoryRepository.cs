using Dapper;
using Roma.Domain.Categories;
using Roma.Infrastructure.Database;

namespace Roma.Infrastructure.Categories;

public class CategoryRepository : ICategoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CategoryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, created_at, deleted_at FROM categories WHERE deleted_at IS NULL ORDER BY name COLLATE NOCASE ASC;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await conn.QueryAsync<CategoryDbRow>(sql);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<Category?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, created_at, deleted_at FROM categories WHERE id = @Id AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CategoryDbRow>(sql, new { Id = id });
        return row?.ToDomain();
    }

    public async Task<Category?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        const string sql = "SELECT id, name, created_at, deleted_at FROM categories WHERE name = @Name COLLATE NOCASE AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CategoryDbRow>(sql, new { Name = name.Trim() });
        return row?.ToDomain();
    }

    public async Task<Category> AddAsync(Category category, CancellationToken ct = default)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);

        const string checkSql = "SELECT id, name, created_at, deleted_at FROM categories WHERE name = @Name COLLATE NOCASE LIMIT 1;";
        var existingRow = await conn.QuerySingleOrDefaultAsync<CategoryDbRow>(checkSql, new { Name = category.Name.Trim() });
        if (existingRow != null)
        {
            if (existingRow.Deleted_At != null)
            {
                await conn.ExecuteAsync("UPDATE categories SET deleted_at = NULL WHERE id = @Id;", new { Id = existingRow.Id });
                return Category.Reconstitute(existingRow.Id, existingRow.Name, DateTimeOffset.Parse(existingRow.Created_At));
            }
        }

        const string sql = """
            INSERT INTO categories (name, created_at)
            VALUES (@Name, @CreatedAt)
            RETURNING id;
            """;
        var id = await conn.ExecuteScalarAsync<int>(sql, new
        {
            Name = category.Name.Trim(),
            CreatedAt = category.CreatedAt.ToString("o")
        });

        return Category.Reconstitute(id, category.Name, category.CreatedAt);
    }

    public async Task<bool> UpdateAsync(Category category, CancellationToken ct = default)
    {
        const string sql = "UPDATE categories SET name = @Name WHERE id = @Id AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { Id = category.Id, Name = category.Name });
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        const string sql = "UPDATE categories SET deleted_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id = @Id AND deleted_at IS NULL;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { Id = id });
        return affected > 0;
    }

    public async Task<bool> HasTasksReferencingCategoryAsync(int id, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM tasks WHERE category_id = @Id AND deleted_at IS NULL LIMIT 1;";
        using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var exists = await conn.ExecuteScalarAsync<int?>(sql, new { Id = id });
        return exists.HasValue;
    }

    private class CategoryDbRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Created_At { get; set; } = string.Empty;
        public string? Deleted_At { get; set; }

        public Category ToDomain() =>
            Category.Reconstitute(Id, Name, DateTimeOffset.Parse(Created_At));
    }
}
