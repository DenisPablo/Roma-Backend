using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Roma.Application.Categories;

namespace Roma.Tests.Api;

public class CategoriesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDb;

    public CategoriesApiTests(WebApplicationFactory<Program> factory)
    {
        _tempDb = Path.Combine(Path.GetTempPath(), $"roma_cat_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", _tempDb);
            builder.UseSetting("Roma:Categories", "Trabajo,Estudio,Salud");
        });
    }

    [Fact]
    public async Task Categories_FullCrudLifecycle()
    {
        var client = _factory.CreateClient();

        // 1. Initial seeded categories
        var listResponse = await client.GetAsync("/api/categories");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialList = await listResponse.Content.ReadFromJsonAsync<List<CategoryResponse>>();
        initialList.Should().NotBeNull();
        initialList!.Select(c => c.Name).Should().Contain(new[] { "Trabajo", "Estudio", "Salud" });

        // 2. Create new category
        var createResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Deportes"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Deportes");

        // 3. Duplicate creation fails with 409
        var dupResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("deportes"));
        dupResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Update category
        var updateResponse = await client.PatchAsJsonAsync($"/api/categories/{created.Id}", new UpdateCategoryRequest("Fitness"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        updated!.Name.Should().Be("Fitness");

        // 5. Delete category
        var deleteResponse = await client.DeleteAsync($"/api/categories/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 6. Verify deleted via API
        var getDeleted = await client.GetAsync($"/api/categories/{created.Id}");
        getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 7. Verify soft delete in DB (record still exists physically with deleted_at set)
        using var conn = new SqliteConnection($"Data Source={_tempDb}");
        var deletedAt = await conn.ExecuteScalarAsync<string>("SELECT deleted_at FROM categories WHERE id = @Id", new { Id = created.Id });
        deletedAt.Should().NotBeNullOrEmpty();
    }
}
