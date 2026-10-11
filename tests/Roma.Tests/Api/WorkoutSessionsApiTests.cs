using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Roma.Application.Gym;

namespace Roma.Tests.Api;

public class WorkoutSessionsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WorkoutSessionsApiTests(WebApplicationFactory<Program> factory)
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"roma_gym_api_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", tempDb);
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

        var createRes = await client.PostAsJsonAsync("/workouts", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        createRes.Headers.Location.Should().NotBeNull();

        var created = await createRes.Content.ReadFromJsonAsync<WorkoutSessionResponse>();
        created.Should().NotBeNull();
        created!.Id.Should().BeGreaterThan(0);
        created.TargetMuscleGroup.Should().Be("Legs");
        created.Rpe.Should().Be(8);
        created.Feeling.Should().Be(4);
        created.RecoveryScore.Should().Be(3);

        // 2. Out of bounds validation (RPE = 12) -> 400
        var invalidReq = new CreateWorkoutSessionRequest("2026-10-15", "Legs", 12, 4, 3);
        var invalidRes = await client.PostAsJsonAsync("/workouts", invalidReq);
        invalidRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Lookup by ID
        var getByIdRes = await client.GetAsync($"/workouts/{created.Id}");
        getByIdRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Lookup non-existent -> 404
        var notFoundRes = await client.GetAsync("/workouts/99999");
        notFoundRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Inverted date range query -> 400
        var invertedRes = await client.GetAsync("/workouts?from=2026-10-20&to=2026-10-10");
        invertedRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Query matching range -> 200
        var listRes = await client.GetAsync("/workouts?from=2026-10-01&to=2026-10-31");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listRes.Content.ReadFromJsonAsync<List<WorkoutSessionResponse>>();
        list.Should().NotBeNull();
        list.Should().Contain(s => s.Id == created.Id);

        // 6. Delete session
        var deleteRes = await client.DeleteAsync($"/workouts/{created.Id}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted -> 404
        var recheckRes = await client.GetAsync($"/workouts/{created.Id}");
        recheckRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
