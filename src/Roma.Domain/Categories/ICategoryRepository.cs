namespace Roma.Domain.Categories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
    Task<Category?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Category?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<Category> AddAsync(Category category, CancellationToken ct = default);
    Task<bool> UpdateAsync(Category category, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    Task<bool> HasTasksReferencingCategoryAsync(int id, CancellationToken ct = default);
}
