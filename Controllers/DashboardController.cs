using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;

namespace MovieApi.Controllers
{
    [Authorize(Roles = "admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var summary = await _context.VDashboardSummaries.ToListAsync();
            return Ok(summary.FirstOrDefault());
        }

        [HttpGet("movies-per-user")]
        public async Task<IActionResult> GetMoviesPerUser()
        {
            var data = await _context.VMoviesPerUsers.ToListAsync();
            return Ok(data);
        }

        [HttpGet("recent-movies")]
        public async Task<IActionResult> GetRecentMovies()
        {
            var data = await _context.VRecentMovies.ToListAsync();
            return Ok(data);
        }

        [HttpGet("errors-by-action")]
        public async Task<IActionResult> GetErrorsByAction()
        {
            var data = await _context.VErrorsByActions.ToListAsync();
            return Ok(data);
        }
    }
}
