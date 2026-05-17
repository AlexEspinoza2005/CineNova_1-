using Microsoft.AspNetCore.Mvc;

namespace MovieApi.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
