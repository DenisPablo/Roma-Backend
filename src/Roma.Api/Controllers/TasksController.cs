using Microsoft.AspNetCore.Mvc;
using Roma.Application.Tasks;

namespace Roma.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly TaskService _service;

    public TasksController(TaskService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? date,
        [FromQuery] string? day,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? month,
        [FromQuery] int? categoryId,
        [FromQuery] string? status,
        [FromQuery] string? kind,
        CancellationToken ct)
    {
        var targetDate = !string.IsNullOrWhiteSpace(date) ? date : day;
        var list = await _service.GetTasksAsync(targetDate, from, to, month, categoryId, status, kind, ct);
        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var task = await _service.GetByIdAsync(id, ct);
        return Ok(task);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPatch("{id:int}")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskRequest request, CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        var task = await _service.StartAsync(id, ct);
        return Ok(task);
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var result = await _service.CompleteAsync(id, ct);
        return Ok(result);
    }
}
