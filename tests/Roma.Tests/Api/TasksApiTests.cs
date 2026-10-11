using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Roma.Application.Categories;
using Roma.Application.Tasks;

namespace Roma.Tests.Api;

public class TasksApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TasksApiTests(WebApplicationFactory<Program> factory)
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"roma_task_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", tempDb);
            builder.UseSetting("Roma:Categories", "Trabajo,Estudio");
        });
    }

    [Fact]
    public async Task Tasks_FullLifecycle_CreateStartCompleteAndDashboard()
    {
        var client = _factory.CreateClient();

        // 1. Fetch category ID
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/categories");
        catList.Should().NotBeNull();
        var catId = catList![0].Id;

        // 2. Create task
        var createReq = new CreateTaskRequest("Write tests", "2026-10-15", "14:00", catId);
        var createRes = await client.PostAsJsonAsync("/tasks", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var task = await createRes.Content.ReadFromJsonAsync<TaskResponse>();
        task.Should().NotBeNull();
        task!.Status.Should().Be("pending");
        task.Kind.Should().Be("task");

        // 3. Query dashboard by day
        var dayList = await client.GetFromJsonAsync<List<TaskResponse>>("/tasks?day=2026-10-15");
        dayList.Should().NotBeNull();
        dayList!.Should().Contain(t => t.Id == task.Id && t.Title == "Write tests");

        // 4. Start task
        var startRes = await client.PostAsync($"/tasks/{task.Id}/start", null);
        startRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var startedTask = await startRes.Content.ReadFromJsonAsync<TaskResponse>();
        startedTask!.Status.Should().Be("in_progress");
        startedTask.StartedAt.Should().NotBeNull();

        // 5. Trying to edit schedule while in_progress returns 409 Conflict
        var patchSchedRes = await client.PatchAsJsonAsync($"/tasks/{task.Id}", new UpdateTaskRequest(null, "2026-10-16", "15:00", null));
        patchSchedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 6. Complete task
        var completeRes = await client.PostAsync($"/tasks/{task.Id}/complete", null);
        completeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeData = await completeRes.Content.ReadFromJsonAsync<CompleteTaskResponse>();
        completeData.Should().NotBeNull();
        completeData!.Id.Should().Be(task.Id);
        completeData.FormattedDuration.Should().NotBeNullOrEmpty();

        // 7. Verify task in completed state
        var getRes = await client.GetFromJsonAsync<TaskResponse>($"/tasks/{task.Id}");
        getRes.Should().NotBeNull();
        getRes!.Status.Should().Be("completed");
        getRes.CompletedAt.Should().NotBeNull();
        getRes.DurationSeconds.Should().NotBeNull();
    }
}
