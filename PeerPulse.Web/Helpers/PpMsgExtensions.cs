using Microsoft.AspNetCore.Mvc;

namespace PeerPulse.Web.Helpers
{
    // Usage in any controller:  this.PpSuccess("Profile saved.");  return RedirectToAction("Index");
    public static class PpMsgExtensions
    {
        public static void PpSuccess(this Controller c, string text) => Set(c, "success", text);
        public static void PpWarning(this Controller c, string text) => Set(c, "warning", text);
        public static void PpError(this Controller c, string text) => Set(c, "error", text);
        public static void PpInfo(this Controller c, string text) => Set(c, "info", text);

        private static void Set(Controller c, string type, string text)
        {
            c.TempData["PpMsg"] = text;
            c.TempData["PpMsgType"] = type;
        }
    }
}