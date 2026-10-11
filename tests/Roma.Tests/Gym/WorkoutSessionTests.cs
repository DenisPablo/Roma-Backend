using FluentAssertions;
using Roma.Domain.Exceptions;
using Roma.Domain.Gym;

namespace Roma.Tests.Gym;

public class WorkoutSessionTests
{
    [Fact]
    public void Create_ValidInputs_ReturnsSessionWithExpectedProperties()
    {
        var session = WorkoutSession.Create(
            date: "2026-10-15",
            targetMuscleGroup: "Legs",
            rpe: 8,
            feeling: 4,
            recoveryScore: 3,
            notes: "Heavy squat session");

        session.Date.Should().Be("2026-10-15");
        session.TargetMuscleGroup.Should().Be("Legs");
        session.Rpe.Should().Be(8);
        session.Feeling.Should().Be(4);
        session.RecoveryScore.Should().Be(3);
        session.Notes.Should().Be("Heavy squat session");
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public void Create_InvalidRpe_ThrowsDomainValidationException(int invalidRpe)
    {
        var act = () => WorkoutSession.Create("2026-10-15", "Back", invalidRpe, 3, 3);
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*RPE must be an integer between 1 and 10*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_InvalidFeeling_ThrowsDomainValidationException(int invalidFeeling)
    {
        var act = () => WorkoutSession.Create("2026-10-15", "Chest", 7, invalidFeeling, 3);
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Feeling score must be an integer between 1 and 5*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_InvalidRecovery_ThrowsDomainValidationException(int invalidRecovery)
    {
        var act = () => WorkoutSession.Create("2026-10-15", "Chest", 7, 3, invalidRecovery);
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Recovery score must be an integer between 1 and 5*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyMuscleGroup_ThrowsDomainValidationException(string emptyGroup)
    {
        var act = () => WorkoutSession.Create("2026-10-15", emptyGroup, 7, 3, 3);
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Target muscle group cannot be empty*");
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    [InlineData("15/10/2026")]
    public void Create_InvalidDate_ThrowsDomainValidationException(string invalidDate)
    {
        var act = () => WorkoutSession.Create(invalidDate, "Shoulders", 7, 3, 3);
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Date*");
    }
}
