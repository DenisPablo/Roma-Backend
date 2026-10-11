using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Roma.Application.Categories;
using Roma.Application.Tasks;

namespace Roma.Tests.Api;

public class TasksApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDb;

    public TasksApiTests(WebApplicationFactory<Program> factory)
    {
        _tempDb = Path.Combine(Path.GetTempPath(), $"roma_task_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", _tempDb);
            builder.UseSetting("Roma:Categories", "Trabajo,Estudio");
        });
    }

    [Fact]
    public async Task Tasks_FullLifecycle_CreateStartCompleteAndDashboard()
    {
        var client = _factory.CreateClient();

        // 1. Fetch category ID
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        catList.Should().NotBeNull();
        var catId = catList![0].Id;

        // 2. Create task
        var createReq = new CreateTaskRequest("Write tests", "2026-10-15", "14:00", catId);
        var createRes = await client.PostAsJsonAsync("/api/tasks", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var task = await createRes.Content.ReadFromJsonAsync<TaskResponse>();
        task.Should().NotBeNull();
        task!.Status.Should().Be("pending");
        task.Kind.Should().Be("task");

        // 3. Query dashboard by day
        var dayList = await client.GetFromJsonAsync<List<TaskResponse>>("/api/tasks?day=2026-10-15");
        dayList.Should().NotBeNull();
        dayList!.Should().Contain(t => t.Id == task.Id && t.Title == "Write tests");

        // 4. Start task
        var startRes = await client.PostAsync($"/api/tasks/{task.Id}/start", null);
        startRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var startedTask = await startRes.Content.ReadFromJsonAsync<TaskResponse>();
        startedTask!.Status.Should().Be("in_progress");
        startedTask.StartedAt.Should().NotBeNull();

        // 5. Trying to edit schedule while in_progress returns 409 Conflict
        var patchSchedRes = await client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(null, "2026-10-16", "15:00", null));
        patchSchedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 6. Complete task
        var completeRes = await client.PostAsync($"/api/tasks/{task.Id}/complete", null);
        completeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeData = await completeRes.Content.ReadFromJsonAsync<CompleteTaskResponse>();
        completeData.Should().NotBeNull();
        completeData!.Id.Should().Be(task.Id);
        completeData.FormattedDuration.Should().NotBeNullOrEmpty();

        // 7. Verify task in completed state
        var getRes = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}");
        getRes.Should().NotBeNull();
        getRes!.Status.Should().Be("completed");
        getRes.CompletedAt.Should().NotBeNull();
        getRes.DurationSeconds.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllTasks_ReturnsAllTasks()
    {
        var client = _factory.CreateClient();
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task 1", "2026-10-10", "10:00", catId));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task 2", "2026-10-15", "11:00", catId));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task 3", "2026-10-20", "12:00", catId));

        var res = await client.GetAsync("/api/tasks");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await res.Content.ReadFromJsonAsync<List<TaskResponse>>();
        tasks.Should().NotBeNull();
        tasks!.Should().Contain(t => t.Title == "Task 1");
        tasks.Should().Contain(t => t.Title == "Task 2");
        tasks.Should().Contain(t => t.Title == "Task 3");
    }

    [Fact]
    public async Task GetTasksByDate_ReturnsMatchingTasks()
    {
        var client = _factory.CreateClient();
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task A", "2026-10-12", "09:00", catId));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task B", "2026-10-13", "10:00", catId));

        var res = await client.GetAsync("/api/tasks?date=2026-10-12");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await res.Content.ReadFromJsonAsync<List<TaskResponse>>();
        tasks.Should().NotBeNull();
        tasks!.Should().Contain(t => t.Title == "Task A");
        tasks.Should().NotContain(t => t.Title == "Task B");
    }

    [Fact]
    public async Task GetTasksByRange_ReturnsMatchingRange()
    {
        var client = _factory.CreateClient();
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Early Task", "2026-10-05", "09:00", catId));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("In Range Task", "2026-10-15", "10:00", catId));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Late Task", "2026-10-25", "11:00", catId));

        var res = await client.GetAsync("/api/tasks?from=2026-10-10&to=2026-10-20");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await res.Content.ReadFromJsonAsync<List<TaskResponse>>();
        tasks.Should().NotBeNull();
        tasks!.Should().Contain(t => t.Title == "In Range Task");
        tasks.Should().NotContain(t => t.Title == "Early Task");
        tasks.Should().NotContain(t => t.Title == "Late Task");
    }

    [Fact]
    public async Task InvertedRange_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/api/tasks?from=2026-10-25&to=2026-10-10");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteTask_PerformsSoftDelete_AndExcludesFromQueries()
    {
        var client = _factory.CreateClient();
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        // 1. Create task
        var createRes = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task to delete", "2026-10-18", "16:00", catId));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<TaskResponse>();
        created.Should().NotBeNull();

        // 2. Delete task via API
        var deleteRes = await client.DeleteAsync($"/api/tasks/{created!.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. GET by ID returns 404
        var getRes = await client.GetAsync($"/api/tasks/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Query list excludes deleted task
        var list = await client.GetFromJsonAsync<List<TaskResponse>>("/api/tasks?date=2026-10-18");
        list.Should().NotContain(t => t.Id == created.Id);

        // 5. Verify direct physical DB record still exists with deleted_at set
        using var conn = new SqliteConnection($"Data Source={_tempDb}");
        var deletedAt = await conn.ExecuteScalarAsync<string>("SELECT deleted_at FROM tasks WHERE id = @Id", new { Id = created.Id });
        deletedAt.Should().NotBeNullOrEmpty();
    }
}
