using Microsoft.AspNetCore.Mvc;
using PeerPulse.Web.ViewModels.Shared;

namespace PeerPulse.Web.ViewComponents;

public sealed class ModelPopupViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string id,string theme,string title,string message,string primaryText = "Close",string? primaryUrl = null,string? secondaryText = null,string? secondaryUrl = null)
    {
        string safeTheme = theme?.Trim().ToLowerInvariant() switch
        {
            "success" => "success",
            "warning" => "warning",
            "danger" => "danger",
            "secondary" => "secondary",
            _ => "primary"
        };

        string icon = safeTheme switch
        {
            "success" => "✓",
            "warning" => "!",
            "danger" => "×",
            "secondary" => "•",
            _ => "i"
        };

        return View(new PopupViewModel
        {
            Id = id,
            Theme = safeTheme,
            Icon = icon,
            Title = title,
            Message = message,
            PrimaryText = primaryText,
            PrimaryUrl = primaryUrl,
            SecondaryText = secondaryText,
            SecondaryUrl = secondaryUrl
        });
    }
}