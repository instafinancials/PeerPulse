using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PeerPulse.Web.Services;

// Applies to the PeerPulse application cookie only, never the external Google cookie.
public static class PeerPulseSessionPolicy
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    private const string StartedKey = "PeerPulse.SessionStartedUnix";

    public static Task SigningIn(CookieSigningInContext context)
    {
        var now = DateTimeOffset.UtcNow;
        context.Properties.Items[StartedKey] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        context.Properties.IssuedUtc = now;
        context.Properties.ExpiresUtc = now.Add(IdleLifetime);
        context.Properties.AllowRefresh = true;
        // Keep the login form's IsPersistent choice. Both choices still have the
        // same 20-minute sliding ticket and 8-hour absolute authentication limit.
        return Task.CompletedTask;
    }

    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var valid = context.Properties.Items.TryGetValue(StartedKey, out var text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            && started <= now + 60
            && now - started < (long)AbsoluteLifetime.TotalSeconds;

        if (!valid)
        {
            // Existing cookies without StartedKey must sign in once after rollout.
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }
}
