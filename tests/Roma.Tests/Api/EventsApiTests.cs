using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Roma.Application.Categories;
using Roma.Application.Tasks;

namespace Roma.Tests.Api;

public class EventsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EventsApiTests(WebApplicationFactory<Program> factory)
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"roma_evt_test_{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Roma:DbPath", tempDb);
            builder.UseSetting("Roma:Categories", "General");
            builder.UseSetting("Roma:EventReminderOffsets", "-1d,-2h,0");
        });
    }

    [Fact]
    public async Task Event_CreationRemindersAndImmutability()
    {
        var client = _factory.CreateClient();

        var catList = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories");
        var catId = catList![0].Id;

        // 1. Create event with default configured reminders
        var createReq = new CreateTaskRequest("Dentist", "2026-10-25", "11:00", catId, Kind: "event");
        var createRes = await client.PostAsJsonAsync("/api/tasks", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var evt = await createRes.Content.ReadFromJsonAsync<TaskResponse>();
        evt.Should().NotBeNull();
        evt!.Kind.Should().Be("event");
        evt.Reminders.Should().NotBeNull();
        evt.Reminders!.Should().HaveCount(3);
        evt.Reminders!.Select(r => r.OffsetMinutes).Should().Equal(-1440, -120, 0);

        // 2. Starting an event returns 409 Conflict
        var startRes = await client.PostAsync($"/api/tasks/{evt.Id}/start", null);
        startRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Completing an event returns 409 Conflict
        var completeRes = await client.PostAsync($"/api/tasks/{evt.Id}/complete", null);
        completeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Update event schedule
        var patchRes = await client.PatchAsJsonAsync($"/api/tasks/{evt.Id}", new UpdateTaskRequest(null, "2026-10-30", "15:00", null));
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await patchRes.Content.ReadFromJsonAsync<TaskResponse>();
        updated!.Day.Should().Be("2026-10-30");
        updated.ScheduledTime.Should().Be("15:00");
        updated.Reminders!.Select(r => r.DueAt).Should().Equal("2026-10-29T15:00", "2026-10-30T13:00", "2026-10-30T15:00");

        // 5. Update event with new date, time, title, and custom reminders via PUT
        var putRes = await client.PutAsJsonAsync($"/api/tasks/{evt.Id}", new UpdateTaskRequest(
            Title: "Dentist Appointment",
            Day: "2026-11-01",
            ScheduledTime: "09:30",
            CategoryId: catId,
            Reminders: new[] { "-30m", "0" }));
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedWithCustomReminders = await putRes.Content.ReadFromJsonAsync<TaskResponse>();
        updatedWithCustomReminders.Should().NotBeNull();
        updatedWithCustomReminders!.Title.Should().Be("Dentist Appointment");
        updatedWithCustomReminders.Day.Should().Be("2026-11-01");
        updatedWithCustomReminders.ScheduledTime.Should().Be("09:30");
        updatedWithCustomReminders.Reminders.Should().NotBeNull();
        updatedWithCustomReminders.Reminders!.Should().HaveCount(2);
        updatedWithCustomReminders.Reminders!.Select(r => r.OffsetMinutes).Should().Equal(-30, 0);
        updatedWithCustomReminders.Reminders!.Select(r => r.DueAt).Should().Equal("2026-11-01T09:00", "2026-11-01T09:30");

        // 6. Verify persistence via GET /api/tasks/{id}
        var getRes = await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{evt.Id}");
        getRes.Should().NotBeNull();
        getRes!.Title.Should().Be("Dentist Appointment");
        getRes.Reminders!.Select(r => r.OffsetMinutes).Should().Equal(-30, 0);
    }
}
