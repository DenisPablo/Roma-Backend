using Roma.Domain.Categories;
using Roma.Domain.Exceptions;

namespace Roma.Application.Categories;

public record CreateCategoryRequest(string Name);
public record UpdateCategoryRequest(string Name);
public record CategoryResponse(int Id, string Name, DateTimeOffset CreatedAt);

public class CategoryService
{
    private readonly ICategoryRepository _repository;

    public CategoryService(ICategoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return list.Select(c => new CategoryResponse(c.Id, c.Name, c.CreatedAt)).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var category = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Category with ID {id} was not found.");

        return new CategoryResponse(category.Id, category.Name, category.CreatedAt);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var existing = await _repository.GetByNameAsync(request.Name, ct);
        if (existing != null)
            throw new DomainConflictException($"Category '{request.Name}' already exists.");

        var entity = Category.Create(request.Name);
        var created = await _repository.AddAsync(entity, ct);
        return new CategoryResponse(created.Id, created.Name, created.CreatedAt);
    }

    public async Task<CategoryResponse> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Category with ID {id} was not found.");

        var existing = await _repository.GetByNameAsync(request.Name, ct);
        if (existing != null && existing.Id != id)
            throw new DomainConflictException($"Category '{request.Name}' already exists.");

        category.UpdateName(request.Name);
        await _repository.UpdateAsync(category, ct);
        return new CategoryResponse(category.Id, category.Name, category.CreatedAt);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Category with ID {id} was not found.");

        var hasTasks = await _repository.HasTasksReferencingCategoryAsync(id, ct);
        if (hasTasks)
            throw new DomainConflictException($"Cannot delete category '{category.Name}' because tasks are referencing it.");

        await _repository.DeleteAsync(id, ct);
    }
}
