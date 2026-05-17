using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models.Views;
using System.Security.Claims;

namespace MovieApi.Controllers
{
    [Authorize(Roles = "admin")]
    public class DashboardViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Route("Dashboard")]
        public async Task<IActionResult> Index()
        {
            var summary = await _context.VDashboardSummaries.FirstOrDefaultAsync() ?? new VDashboardSummary();
            
            ViewBag.MoviesPerUser = await _context.VMoviesPerUsers
                .OrderByDescending(u => u.MovieCount)
                .Take(10)
                .ToListAsync();

            ViewBag.RecentMovies = await _context.VRecentMovies
                .OrderByDescending(m => m.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.ErrorsByAction = await _context.VErrorsByActions
                .OrderByDescending(e => e.Total)
                .ToListAsync();

            // Functional data
            var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            
            var recentLogs = await _context.OperationLogs
                .Include(l => l.Profile)
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .ToListAsync();

            foreach (var log in recentLogs)
                log.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(log.CreatedAt, ecuadorZone);

            ViewBag.RecentLogs = recentLogs;

            var recentReviews = await _context.MovieReviews
                .Include(r => r.Movie)
                .Include(r => r.Profile)
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .ToListAsync();

            foreach (var review in recentReviews)
                review.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(review.CreatedAt, ecuadorZone);

            ViewBag.RecentReviews = recentReviews;

            var recentQueries = await _context.AgentQueries
                .Include(q => q.Profile)
                .OrderByDescending(q => q.CreatedAt)
                .Take(10)
                .ToListAsync();

            foreach (var query in recentQueries)
                query.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(query.CreatedAt, ecuadorZone);

            ViewBag.RecentQueries = recentQueries;

            ViewBag.Users = await _context.Profiles
                .Include(p => p.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .OrderByDescending(p => p.CreatedAt)
                .Take(20)
                .ToListAsync();
            
            return View(summary);
        }
    }
}
