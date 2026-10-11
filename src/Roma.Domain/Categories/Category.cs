using Roma.Domain.Exceptions;

namespace Roma.Domain.Categories;

public class Category
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private Category() { }

    public static Category Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Category name cannot be empty.");

        return new Category
        {
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Category Reconstitute(int id, string name, DateTimeOffset createdAt)
    {
        return new Category
        {
            Id = id,
            Name = name,
            CreatedAt = createdAt
        };
    }

    public void UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainValidationException("Category name cannot be empty.");

        Name = newName.Trim();
    }
}
