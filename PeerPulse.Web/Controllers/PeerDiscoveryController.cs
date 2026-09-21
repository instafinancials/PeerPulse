using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PeerPulse.Web.Controllers
{
    [Authorize]
    [Route("PeerDiscovery")]

    public class PeerDiscoveryController : Controller
    {
        [HttpGet("Add")]
        public IActionResult Add()
        {
            ViewData["DashboardPage"] = "add-peer";
            return View();
        }

        [HttpGet("Manage")]
        public IActionResult Manage()
        {
            ViewData["DashboardPage"] = "manage-peers";
            return View();
        }

        [HttpGet("Groups")]
        public IActionResult Groups()
        {
            ViewData["DashboardPage"] = "peer-groups";
            return View();
        }
    }
}
