using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;

namespace MovieApi.Controllers
{
    [Authorize(Roles = "admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class OperationLogsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public OperationLogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetLogs(
            [FromQuery] string? status,
            [FromQuery] string? action,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var query = _context.OperationLogs.AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(l => l.Status == status);

            if (!string.IsNullOrEmpty(action))
                query = query.Where(l => l.Action == action);

            if (desde.HasValue)
                query = query.Where(l => l.CreatedAt >= desde.Value);

            if (hasta.HasValue)
                query = query.Where(l => l.CreatedAt <= hasta.Value);

            var logs = await query.OrderByDescending(l => l.CreatedAt)
                                .Take(100)
                                .ToListAsync();

            return Ok(logs);
        }
    }
}
