using Microsoft.AspNetCore.Mvc;
using PeerPulse.Web.ViewModels.PeerDiscovery;

namespace PeerPulse.Web.ViewComponents
{
    public sealed class CreatePeerGroupViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(string? returnUrl = null)
        {
            var model = new CreatePeerGroupViewModel
            {
                ReturnUrl = returnUrl
            };

            return View("~/Views/Shared/Components/CreatePeerGroup/Default.cshtml",model);
        }
    }
}
