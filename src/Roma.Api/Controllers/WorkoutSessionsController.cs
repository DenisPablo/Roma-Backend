using Microsoft.AspNetCore.Mvc;
using Roma.Application.Gym;

namespace Roma.Api.Controllers;

[ApiController]
[Route("api/workouts")]
public class WorkoutSessionsController : ControllerBase
{
    private readonly WorkoutSessionService _service;

    public WorkoutSessionsController(WorkoutSessionService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkoutSessionRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetSessions([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? month, CancellationToken ct)
    {
        var list = await _service.GetSessionsAsync(from, to, month, ct);
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var session = await _service.GetByIdAsync(id, ct);
        return Ok(session);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWorkoutSessionRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
