using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeerPulse.Web.Models.InstaUsers;
using PeerPulse.Web.Services;
using PeerPulse.Web.ViewModels.Account;
using System.Security.Claims;

namespace PeerPulse.Web.Controllers;

// This controller signs people in, so none of its pages can require a signed-in user.
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController : Controller
{
    private const string PeerPulseScheme = "PeerPulse";
    private const string ExternalScheme = "PeerPulse.External";

    private readonly ILoginService _loginService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(ILoginService loginService, ISubscriptionService subscriptionService, ILogger<AccountController> logger)
    {
        _loginService = loginService;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null, string? reason = null)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;

        if (User.Identity?.IsAuthenticated == true)
        {
            return safeReturnUrl is not null
                ? LocalRedirect(safeReturnUrl)
                : RedirectToAction("Index", "Dashboard");
        }

        // Added by Program.cs when a page request arrives with an expired sign-in cookie.
        if (string.Equals(reason, "expired", StringComparison.OrdinalIgnoreCase))
        {
            SetPopup(
                "warning",
                "Session expired",
                "For your security you were signed out. Please sign in again to continue.",
                "Close");
        }

        return View(new LoginViewModel
        {
            ReturnUrl = safeReturnUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Identifier) || string.IsNullOrWhiteSpace(model.Password))
        {
            SetPopup(
                "warning",
                "Enter login details",
                "Enter your email address or mobile number and password.",
                "Close");

            model.Password = string.Empty;
            return View(model);
        }

        try
        {
            LoginCheckResult result = await _loginService.ValidateAsync(model.Identifier, model.Password, cancellationToken);

            model.Password = string.Empty;
            ModelState.Remove(nameof(LoginViewModel.Password));

            switch (result.Status)
            {
                case LoginCheckStatus.EmailNotFound:
                    SetPopup("warning", "Invalid Email Address",
                        "Enter a valid registered email address.", "Try again");
                    return View(model);

                case LoginCheckStatus.MobileNotFound:
                    SetPopup("warning", "Invalid Mobile Number",
                        "Enter a valid registered 10-digit mobile number.", "Try again");
                    return View(model);

                case LoginCheckStatus.InvalidPassword:
                    SetPopup("danger", "Invalid credentials",
                        "The password you entered is incorrect.", "Try again");
                    return View(model);

                case LoginCheckStatus.InvalidIdentifier:
                    SetPopup("warning", "Invalid login details",
                        "Enter a valid email address or 10-digit mobile number.", "Close");
                    return View(model);

                case LoginCheckStatus.Disabled:
                    SetPopup("danger", "Account disabled",
                        "Your account is disabled. Please contact PeerPulse support for assistance.",
                        "Contact support",
                        "mailto:support@instafinancials.com",
                        "Close");
                    return View(model);

                case LoginCheckStatus.VerificationRequired:
                    TempData["VerificationUserId"] = result.User!.InstaUserId;

                    // NOTE: Url.Action returns null while no StartVerification action exists;
                    // the button then just closes the popup.
                    SetPopup("warning", "Verification required",
                        "Please verify your registered contact details before accessing PeerPulse.",
                        "Verify now",
                        Url.Action("StartVerification", "Account"),
                        "Close");
                    return View(model);

                case LoginCheckStatus.Error:
                    SetPopup("danger", "Unable to sign in",
                        "Please try again in a few minutes.", "Close");
                    return View(model);

                case LoginCheckStatus.Success:
                    await _subscriptionService.EnsureStarterSubscriptionAsync(
                        result.User!.InstaUserId,
                        cancellationToken);

                    await SignInUserAsync(
                        result.User.InstaUserId.ToString(),
                        result.User.UserName ?? result.User.UserEmail ?? "PeerPulse User",
                        result.User.UserEmail ?? string.Empty,
                        persistent: model.RememberMe,
                        authenticationMethod: null);

                    if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                    {
                        return LocalRedirect(model.ReturnUrl);
                    }

                    return RedirectToAction("Index", "Dashboard");

                default:
                    SetPopup("danger", "Unable to sign in", "Please try again.", "Close");
                    return View(model);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed. Trace ID: {TraceId}", HttpContext.TraceIdentifier);

            model.Password = string.Empty;

            // A popup, because the Login view does not render ModelState errors.
            SetPopup("danger", "Unable to sign in",
                "Login is temporarily unavailable. Please try again in a few minutes.", "Close");

            return View(model);
        }
    }

    [HttpGet]
    public IActionResult VerifyEmailOtp()
    {
        // Add real email OTP verification in the next step.
        // An inactive user must remain unauthenticated here.
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(PeerPulseScheme);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Denied()
    {
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public IActionResult GoogleLogin(string? returnUrl)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;

        var properties = new GoogleChallengeProperties
        {
            RedirectUri = Url.Action(nameof(GoogleResponse), "Account", new { returnUrl = safeReturnUrl }),
            Prompt = "select_account"
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public async Task<IActionResult> GoogleResponse(string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {
            var externalResult = await HttpContext.AuthenticateAsync(ExternalScheme);

            try
            {
                if (!externalResult.Succeeded || externalResult.Principal is null)
                {
                    SetPopup("danger", "Google sign-in failed",
                        "We could not complete Google sign-in. Please try again.", "Close");

                    return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
                }

                string? googleEmail = externalResult.Principal.FindFirstValue(ClaimTypes.Email);

                if (string.IsNullOrWhiteSpace(googleEmail))
                {
                    SetPopup("warning", "Google email not available",
                        "Your Google account did not provide an email address.", "Close");

                    return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
                }

                // Find the existing PeerPulse/InstaUser account by Gmail address.
                InstaUser? user = await _loginService.FindByEmailAsync(googleEmail.Trim(), cancellationToken);

                if (user is null)
                {
                    SetPopup("warning", "Account not found",
                        "This Google email is not registered with PeerPulse.", "Close");

                    return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
                }

                // Status 2 = permanently disabled: no login and no OTP.
                if (user.StatusId == 2)
                {
                    SetPopup("danger", "Account disabled",
                        "Your account is disabled. Please contact PeerPulse support.",
                        "Contact support",
                        "mailto:support@instafinancials.com",
                        "Close");

                    return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
                }

                // Status 0 / inactive user: send to your OTP verification flow later.
                if (user.StatusId != 1 || user.IsUserActive != true)
                {
                    TempData["VerificationUserId"] = user.InstaUserId;

                    SetPopup("warning", "Verification required",
                        "Verify your registered contact details before accessing PeerPulse.", "Close");

                    return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
                }

                // First successful login creates the Starter plan.
                await _subscriptionService.EnsureStarterSubscriptionAsync(user.InstaUserId, cancellationToken);

                await SignInUserAsync(
                    user.InstaUserId.ToString(),
                    user.UserName ?? user.UserEmail ?? "PeerPulse User",
                    user.UserEmail ?? googleEmail,
                    persistent: false,
                    authenticationMethod: "Google");

                // returnUrl can be null when the Login page was opened directly.
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }
            finally
            {
                // Always remove the temporary Google OAuth cookie.
                await HttpContext.SignOutAsync(ExternalScheme);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google login failed. Trace ID: {TraceId}", HttpContext.TraceIdentifier);

            SetPopup("danger", "Google sign-in failed",
                "We could not complete Google sign-in. Please try again.", "Close");

            return View("Login", new LoginViewModel { ReturnUrl = returnUrl });
        }
    }

    // One place that builds the sign-in cookie for both password and Google sign-in.
    private Task SignInUserAsync(string instaUserId, string displayName, string email, bool persistent, string? authenticationMethod)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, instaUserId),
            new("InstaUserId", instaUserId),          // read by PeerDiscoveryController
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, email)
        };

        if (!string.IsNullOrWhiteSpace(authenticationMethod))
        {
            claims.Add(new Claim(ClaimTypes.AuthenticationMethod, authenticationMethod));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, PeerPulseScheme));

        var properties = new AuthenticationProperties
        {
            IsPersistent = persistent,
            AllowRefresh = true
        };

        // Default: the 15-minute sliding inactivity limit from Program.cs.
        // "Keep me signed in": 24 hours of inactivity.
        if (persistent)
        {
            properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24);
        }

        return HttpContext.SignInAsync(PeerPulseScheme, principal, properties);
    }

    private void SetPopup(string theme, string title, string message, string primaryText, string? primaryUrl = null, string? secondaryText = null)
    {
        ViewData["PopupTheme"] = theme;
        ViewData["PopupTitle"] = title;
        ViewData["PopupMessage"] = message;
        ViewData["PopupPrimaryText"] = primaryText;
        ViewData["PopupPrimaryUrl"] = primaryUrl;
        ViewData["PopupSecondaryText"] = secondaryText;
    }
}