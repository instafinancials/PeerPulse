using Microsoft.AspNetCore.Mvc;

namespace PeerPulse.Web.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
