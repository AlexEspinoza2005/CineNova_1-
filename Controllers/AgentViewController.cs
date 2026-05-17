using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieApi.DTOs;
using MovieApi.Services;

namespace MovieApi.Controllers
{
    [AllowAnonymous]
    public class AgentViewController : Controller
    {
        [Route("Agent")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
