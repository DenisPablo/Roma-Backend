using Roma.Application.Configuration;
using Roma.Domain.Exceptions;
using Roma.Domain.Gym;

namespace Roma.Application.Gym;

public record CreateWorkoutSessionRequest(
    string Date,
    string TargetMuscleGroup,
    int Rpe,
    int Feeling,
    int RecoveryScore,
    string? Notes = null,
    int? DurationMinutes = 60);

public record WorkoutSessionResponse(
    int Id,
    string Date,
    string TargetMuscleGroup,
    int Rpe,
    int Feeling,
    int RecoveryScore,
    string? Notes,
    int? DurationMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public class WorkoutSessionService
{
    private readonly IWorkoutSessionRepository _repository;
    private readonly AppOptions? _options;

    public WorkoutSessionService(IWorkoutSessionRepository repository, AppOptions? options = null)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<WorkoutSessionResponse> CreateAsync(CreateWorkoutSessionRequest request, CancellationToken ct = default)
    {
        var entity = WorkoutSession.Create(
            request.Date,
            request.TargetMuscleGroup,
            request.Rpe,
            request.Feeling,
            request.RecoveryScore,
            request.Notes,
            request.DurationMinutes);

        var created = await _repository.AddAsync(entity, ct);
        return MapToResponse(created);
    }

    public async Task<WorkoutSessionResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var session = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Workout session with ID {id} was not found.");

        return MapToResponse(session);
    }

    public async Task<IReadOnlyList<WorkoutSessionResponse>> GetSessionsAsync(
        string? from = null,
        string? to = null,
        string? month = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(month))
        {
            try
            {
                var tz = _options?.AppTz ?? "America/Argentina/Buenos_Aires";
                var range = Roma.Domain.Common.TimeUtils.ResolveDateRange(month, null, null, tz);
                from = range.From;
                to = range.To;
            }
            catch (ArgumentException ex)
            {
                throw new DomainValidationException(ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            if (string.CompareOrdinal(from, to) > 0)
                throw new DomainValidationException("'from' date cannot be after 'to' date.");
        }

        var list = await _repository.GetSessionsAsync(from, to, ct);
        return list.Select(MapToResponse).ToList();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var session = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Workout session with ID {id} was not found.");

        await _repository.DeleteAsync(id, ct);
    }

    private static WorkoutSessionResponse MapToResponse(WorkoutSession s) =>
        new(
            s.Id,
            s.Date,
            s.TargetMuscleGroup,
            s.Rpe,
            s.Feeling,
            s.RecoveryScore,
            s.Notes,
            s.DurationMinutes,
            s.CreatedAt,
            s.UpdatedAt
        );
}
