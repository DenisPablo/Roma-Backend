using Roma.Domain.Exceptions;
using Roma.Domain.Gym;

namespace Roma.Application.Gym;

public record CreateWorkoutSessionRequest(
    string Date,
    string TargetMuscleGroup,
    int Rpe,
    int Feeling,
    int RecoveryScore,
    string? Notes = null);

public record WorkoutSessionResponse(
    int Id,
    string Date,
    string TargetMuscleGroup,
    int Rpe,
    int Feeling,
    int RecoveryScore,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public class WorkoutSessionService
{
    private readonly IWorkoutSessionRepository _repository;

    public WorkoutSessionService(IWorkoutSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<WorkoutSessionResponse> CreateAsync(CreateWorkoutSessionRequest request, CancellationToken ct = default)
    {
        var entity = WorkoutSession.Create(
            request.Date,
            request.TargetMuscleGroup,
            request.Rpe,
            request.Feeling,
            request.RecoveryScore,
            request.Notes);

        var created = await _repository.AddAsync(entity, ct);
        return MapToResponse(created);
    }

    public async Task<WorkoutSessionResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var session = await _repository.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException($"Workout session with ID {id} was not found.");

        return MapToResponse(session);
    }

    public async Task<IReadOnlyList<WorkoutSessionResponse>> GetSessionsAsync(string? from, string? to, CancellationToken ct = default)
    {
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
            s.CreatedAt,
            s.UpdatedAt
        );
}
