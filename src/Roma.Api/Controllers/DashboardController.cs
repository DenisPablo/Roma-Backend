using Microsoft.AspNetCore.Mvc;
using Roma.Application.Dashboard;

namespace Roma.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _service;

    public DashboardController(DashboardService service)
    {
        _service = service;
    }

    [HttpGet]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] string? month,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var result = await _service.GetStatsAsync(month, from, to, ct);
        return Ok(result);
    }
}
