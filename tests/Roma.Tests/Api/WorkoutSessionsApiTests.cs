using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Roma.Application.Gym;

namespace Roma.Tests.Api;

public class WorkoutSessionsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDb;

    public WorkoutSessionsApiTests(WebApplicationFactory<Program> factory)
    {
        _tempDb = Path.Combine(Path.GetTempPath(), $"roma_gym_api_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", _tempDb);
        });
    }

    [Fact]
    public async Task WorkoutsApi_FullCrudLifecycle_AndQueryFiltering()
    {
        var client = _factory.CreateClient();

        // 1. Create session (Happy path)
        var createReq = new CreateWorkoutSessionRequest(
            Date: "2026-10-15",
            TargetMuscleGroup: "Legs",
            Rpe: 8,
            Feeling: 4,
            RecoveryScore: 3,
            Notes: "Squats and lunges");

        var createRes = await client.PostAsJsonAsync("/api/workouts", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        createRes.Headers.Location.Should().NotBeNull();

        var created = await createRes.Content.ReadFromJsonAsync<WorkoutSessionResponse>();
        created.Should().NotBeNull();
        created!.Id.Should().BeGreaterThan(0);
        created.TargetMuscleGroup.Should().Be("Legs");
        created.Rpe.Should().Be(8);
        created.Feeling.Should().Be(4);
        created.RecoveryScore.Should().Be(3);
        created.DurationMinutes.Should().Be(60);

        // 2. Out of bounds validation (RPE = 12) -> 400
        var invalidReq = new CreateWorkoutSessionRequest("2026-10-15", "Legs", 12, 4, 3);
        var invalidRes = await client.PostAsJsonAsync("/api/workouts", invalidReq);
        invalidRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Lookup by ID
        var getByIdRes = await client.GetAsync($"/api/workouts/{created.Id}");
        getByIdRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Lookup non-existent -> 404
        var notFoundRes = await client.GetAsync("/api/workouts/99999");
        notFoundRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Inverted date range query -> 400
        var invertedRes = await client.GetAsync("/api/workouts?from=2026-10-20&to=2026-10-10");
        invertedRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Query matching range -> 200
        var listRes = await client.GetAsync("/api/workouts?from=2026-10-01&to=2026-10-31");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listRes.Content.ReadFromJsonAsync<List<WorkoutSessionResponse>>();
        list.Should().NotBeNull();
        list.Should().Contain(s => s.Id == created.Id);

        // 5b. Create session with explicit duration
        var customReq = new CreateWorkoutSessionRequest("2026-10-16", "Back", 7, 5, 4, DurationMinutes: 90);
        var customRes = await client.PostAsJsonAsync("/api/workouts", customReq);
        customRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var customCreated = await customRes.Content.ReadFromJsonAsync<WorkoutSessionResponse>();
        customCreated!.DurationMinutes.Should().Be(90);

        // 6. Delete session
        var deleteRes = await client.DeleteAsync($"/api/workouts/{created.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted -> 404
        var recheckRes = await client.GetAsync($"/api/workouts/{created.Id}");
        recheckRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify soft delete in DB (record still exists physically with deleted_at set)
        using var conn = new SqliteConnection($"Data Source={_tempDb}");
        var deletedAt = await conn.ExecuteScalarAsync<string>("SELECT deleted_at FROM workout_sessions WHERE id = @Id", new { Id = created.Id });
        deletedAt.Should().NotBeNullOrEmpty();
    }
}
