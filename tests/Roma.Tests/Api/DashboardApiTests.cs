using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Roma.Application.Categories;
using Roma.Application.Dashboard;
using Roma.Application.Gym;
using Roma.Application.Tasks;

namespace Roma.Tests.Api;

public class DashboardApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDb;

    public DashboardApiTests(WebApplicationFactory<Program> factory)
    {
        _tempDb = Path.Combine(Path.GetTempPath(), $"roma_dashboard_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", _tempDb);
            builder.UseSetting("Roma:Categories", "Trabajo,Estudio,Salud");
        });
    }

    [Fact]
    public async Task GetStats_ReturnsAccurateWorkoutAttendanceTaskCompletionAndCategoryBreakdown()
    {
        var client = _factory.CreateClient();

        // 1. Fetch categories
        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        catList.Should().NotBeNull();
        var catTrabajo = catList!.First(c => c.Name == "Trabajo").Id;
        var catEstudio = catList!.First(c => c.Name == "Estudio").Id;

        // 2. Create tasks
        var t1Res = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Tarea 1", "2026-10-05", "10:00", catTrabajo));
        var t1 = (await t1Res.Content.ReadFromJsonAsync<TaskResponse>())!;
        await client.PostAsync($"/api/tasks/{t1.Id}/start", null);
        await client.PostAsync($"/api/tasks/{t1.Id}/complete", null); // completed

        var t2Res = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Tarea 2", "2026-10-06", "11:00", catTrabajo));
        var t2 = (await t2Res.Content.ReadFromJsonAsync<TaskResponse>())!; // pending

        var t3Res = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Tarea 3", "2026-10-08", "12:00", catEstudio));
        var t3 = (await t3Res.Content.ReadFromJsonAsync<TaskResponse>())!;
        await client.PostAsync($"/api/tasks/{t3.Id}/start", null);
        await client.PostAsync($"/api/tasks/{t3.Id}/complete", null); // completed

        // 3. Create workout sessions
        await client.PostAsJsonAsync("/api/workouts", new CreateWorkoutSessionRequest("2026-10-05", "Pecho", 8, 4, 4));
        await client.PostAsJsonAsync("/api/workouts", new CreateWorkoutSessionRequest("2026-10-07", "Pierna", 9, 3, 3));
        await client.PostAsJsonAsync("/api/workouts", new CreateWorkoutSessionRequest("2026-10-07", "Cardio", 6, 5, 5));

        // 4. Query Dashboard Stats for October 2026
        var statsRes = await client.GetAsync("/api/dashboard/stats?month=2026-10");
        var errorBody = await statsRes.Content.ReadAsStringAsync();
        statsRes.StatusCode.Should().Be(HttpStatusCode.OK, because: errorBody);

        var stats = await statsRes.Content.ReadFromJsonAsync<DashboardStatsResponse>();
        stats.Should().NotBeNull();

        // Verify Period
        stats!.Period.From.Should().Be("2026-10-01");
        stats.Period.To.Should().Be("2026-10-31");
        stats.Period.Month.Should().Be("2026-10");

        // Verify Workout Attendances
        stats.Workouts.TotalSessions.Should().Be(3);
        stats.Workouts.DistinctDaysCount.Should().Be(2); // 2026-10-05 and 2026-10-07
        stats.Workouts.SessionDates.Should().Equal("2026-10-05", "2026-10-07");
        stats.Workouts.AverageRpe.Should().BeApproximately(7.7, 0.1);
        stats.Workouts.ByMuscleGroup.Should().Contain(m => m.MuscleGroup == "Pierna" && m.Count == 1);
        stats.Workouts.ByMuscleGroup.Should().Contain(m => m.MuscleGroup == "Cardio" && m.Count == 1);

        // Verify Task Completion Metrics
        stats.Tasks.TotalTasks.Should().Be(3);
        stats.Tasks.CompletedTasks.Should().Be(2);
        stats.Tasks.PendingTasks.Should().Be(1);
        stats.Tasks.CompletionRate.Should().BeApproximately(66.7, 0.1);

        // Verify Category Breakdown
        var trabajoStats = stats.Categories.First(c => c.CategoryId == catTrabajo);
        trabajoStats.TotalTasks.Should().Be(2);
        trabajoStats.CompletedTasks.Should().Be(1);
        trabajoStats.PendingTasks.Should().Be(1);
        trabajoStats.CompletionRate.Should().Be(50.0);

        var estudioStats = stats.Categories.First(c => c.CategoryId == catEstudio);
        estudioStats.TotalTasks.Should().Be(1);
        estudioStats.CompletedTasks.Should().Be(1);
        estudioStats.PendingTasks.Should().Be(0);
        estudioStats.CompletionRate.Should().Be(100.0);

        // Verify Daily Activity
        stats.DailyActivity.Should().Contain(d => d.Date == "2026-10-05" && d.HadWorkout && d.TasksCompleted == 1);
        stats.DailyActivity.Should().Contain(d => d.Date == "2026-10-06" && !d.HadWorkout && d.TasksPending == 1);
        stats.DailyActivity.Should().Contain(d => d.Date == "2026-10-07" && d.HadWorkout && d.WorkoutCount == 2);
    }

    [Fact]
    public async Task GetStats_WithInvalidMonth_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/dashboard/stats?month=9999-99");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTasks_WithFilters_FiltersByStatusAndCategory()
    {
        var client = _factory.CreateClient();

        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        var tRes = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Filtered Task", "2026-10-12", "09:00", catId));
        var task = (await tRes.Content.ReadFromJsonAsync<TaskResponse>())!;

        // Filter by category
        var listCat = await client.GetFromJsonAsync<List<TaskResponse>>($"/api/tasks?categoryId={catId}&month=2026-10");
        listCat.Should().Contain(t => t.Id == task.Id);

        // Filter by status completed (should not contain pending task)
        var listCompleted = await client.GetFromJsonAsync<List<TaskResponse>>($"/api/tasks?categoryId={catId}&status=completed&month=2026-10");
        listCompleted.Should().NotContain(t => t.Id == task.Id);
    }

    [Fact]
    public async Task GetWorkouts_WithMonthFilter_ReturnsSessionsInMonth()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/workouts", new CreateWorkoutSessionRequest("2026-10-12", "Espalda", 7, 4, 4));

        var sessions = await client.GetFromJsonAsync<List<WorkoutSessionResponse>>("/api/workouts?month=2026-10");
        sessions.Should().NotBeNull();
        sessions!.Should().Contain(s => s.Date == "2026-10-12" && s.TargetMuscleGroup == "Espalda");
    }
}
