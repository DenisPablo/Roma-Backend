using FluentAssertions;
using Roma.Domain.Exceptions;
using Roma.Domain.Tasks;

namespace Roma.Tests.Tasks;

public class TaskItemTests
{
    [Fact]
    public void TaskLifecycle_StartAndComplete_SetsTimestampsAndDuration()
    {
        var task = TaskItem.CreateTask("Study Rust", "2026-10-15", "10:00", 1);
        task.Status.Should().Be("pending");

        var startTime = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        task.Start(startTime);
        task.Status.Should().Be("in_progress");
        task.StartedAt.Should().Be(startTime);

        // Idempotent start
        task.Start(startTime.AddMinutes(5));
        task.Status.Should().Be("in_progress");
        task.StartedAt.Should().Be(startTime);

        // Complete after 45 minutes
        var completeTime = startTime.AddMinutes(45);
        task.Complete(completeTime);

        task.Status.Should().Be("completed");
        task.CompletedAt.Should().Be(completeTime);
        task.DurationSeconds.Should().Be(45 * 60);
    }

    [Fact]
    public void Task_CannotCompleteIfPending()
    {
        var task = TaskItem.CreateTask("Exercise", "2026-10-15", "18:00", 1);

        var act = () => task.Complete(DateTimeOffset.UtcNow);

        act.Should().Throw<DomainConflictException>()
            .WithMessage("*Only in_progress tasks can be completed*");
    }

    [Fact]
    public void Event_CannotBeStartedOrCompleted()
    {
        var evt = TaskItem.CreateEvent("Doctor Appointment", "2026-10-15", "15:00", 1);
        evt.Kind.Should().Be("event");

        var actStart = () => evt.Start(DateTimeOffset.UtcNow);
        actStart.Should().Throw<DomainConflictException>()
            .WithMessage("*Events cannot be transitioned*");

        var actComplete = () => evt.Complete(DateTimeOffset.UtcNow);
        actComplete.Should().Throw<DomainConflictException>()
            .WithMessage("*Events cannot be completed*");
    }

    [Fact]
    public void CloseDay_RolloverTransitionsPendingToNotDone()
    {
        var task = TaskItem.CreateTask("Clean desk", "2026-10-15", "20:00", 1);
        var now = DateTimeOffset.UtcNow;

        task.CloseDay(now);

        task.Status.Should().Be("not_done");
        task.DayClosedAt.Should().Be(now);
    }

    [Fact]
    public void UpdateSchedule_OnlyAllowedWhenPending()
    {
        var task = TaskItem.CreateTask("Meeting", "2026-10-15", "11:00", 1);
        task.MarkReminderSent("2026-10-15T11:00");
        task.ReminderSentAt.Should().NotBeNull();

        // Updating schedule in future resets reminder_sent_at
        task.UpdateSchedule("2026-10-16", "14:00", isFutureInTz: true);
        task.Day.Should().Be("2026-10-16");
        task.ScheduledTime.Should().Be("14:00");
        task.ReminderSentAt.Should().BeNull();

        // Start task
        task.Start(DateTimeOffset.UtcNow);

        // Updating schedule now fails with 409
        var act = () => task.UpdateSchedule("2026-10-17", "10:00", isFutureInTz: true);
        act.Should().Throw<DomainConflictException>();
    }
}
