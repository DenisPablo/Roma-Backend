using System.Globalization;
using System.Text.RegularExpressions;
using Roma.Domain.Exceptions;

namespace Roma.Domain.Gym;

public class WorkoutSession
{
    private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public int Id { get; private set; }
    public string Date { get; private set; } = string.Empty;
    public string TargetMuscleGroup { get; private set; } = string.Empty;
    public int Rpe { get; private set; }
    public int Feeling { get; private set; }
    public int RecoveryScore { get; private set; }
    public string? Notes { get; private set; }
    public int? DurationMinutes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WorkoutSession() { }

    public static WorkoutSession Create(
        string date,
        string targetMuscleGroup,
        int rpe,
        int feeling,
        int recoveryScore,
        string? notes = null,
        int? durationMinutes = 60)
    {
        Validate(date, targetMuscleGroup, rpe, feeling, recoveryScore, notes, durationMinutes);

        var now = DateTimeOffset.UtcNow;
        return new WorkoutSession
        {
            Date = date.Trim(),
            TargetMuscleGroup = targetMuscleGroup.Trim(),
            Rpe = rpe,
            Feeling = feeling,
            RecoveryScore = recoveryScore,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            DurationMinutes = durationMinutes ?? 60,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static WorkoutSession Reconstitute(
        int id,
        string date,
        string targetMuscleGroup,
        int rpe,
        int feeling,
        int recoveryScore,
        string? notes,
        int? durationMinutes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new WorkoutSession
        {
            Id = id,
            Date = date,
            TargetMuscleGroup = targetMuscleGroup,
            Rpe = rpe,
            Feeling = feeling,
            RecoveryScore = recoveryScore,
            Notes = notes,
            DurationMinutes = durationMinutes ?? 60,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    private static void Validate(
        string date,
        string targetMuscleGroup,
        int rpe,
        int feeling,
        int recoveryScore,
        string? notes,
        int? durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(date) || !DateRegex.IsMatch(date.Trim()))
            throw new DomainValidationException("Date must be in format YYYY-MM-DD.");

        if (!DateTime.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new DomainValidationException("Date is not a valid calendar date.");

        if (string.IsNullOrWhiteSpace(targetMuscleGroup))
            throw new DomainValidationException("Target muscle group cannot be empty.");

        if (rpe is < 1 or > 10)
            throw new DomainValidationException("RPE must be an integer between 1 and 10.");

        if (feeling is < 1 or > 5)
            throw new DomainValidationException("Feeling score must be an integer between 1 and 5.");

        if (recoveryScore is < 1 or > 5)
            throw new DomainValidationException("Recovery score must be an integer between 1 and 5.");

        if (notes != null && notes.Length > 1000)
            throw new DomainValidationException("Notes cannot exceed 1000 characters.");

        if (durationMinutes.HasValue && durationMinutes.Value <= 0)
            throw new DomainValidationException("Duration must be a positive number of minutes.");
    }
}
