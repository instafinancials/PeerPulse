using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeerPulse.Web.Services;
using PeerPulse.Web.ViewModels.Account;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Google;
using PeerPulse.Web.Models.InstaUsers;

namespace PeerPulse.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController : Controller
{
    private readonly LoginService _loginService;
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(LoginService loginService,SubscriptionService subscriptionService,ILogger<AccountController> logger)
    {
        _loginService = loginService;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model,CancellationToken cancellationToken)
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
            LoginCheckResult result = await _loginService.ValidateAsync(model.Identifier,model.Password,cancellationToken);

     
            model.Password = string.Empty;
            ModelState.Remove(nameof(LoginViewModel.Password));

            switch (result.Status)
            {
                case LoginCheckStatus.EmailNotFound:
                    SetPopup(
                        "warning",
                        "Invalid Email Address",
                        "Enter a valid registered email address.",
                        "Try again");
                    return View(model);

                case LoginCheckStatus.MobileNotFound:
                    SetPopup(
                        "warning",
                        "Invalid Mobile Number",
                        "Enter a valid registered 10-digit mobile number.",
                        "Try again");
                    return View(model);

                case LoginCheckStatus.InvalidPassword:
                    SetPopup(
                        "danger",
                        "Invalid credentials",
                        "The password you entered is incorrect.",
                        "Try again");
                    return View(model);

                case LoginCheckStatus.InvalidIdentifier:
                    SetPopup(
                        "warning",
                        "Invalid login details",
                        "Enter a valid email address or 10-digit mobile number.",
                        "Close");
                    return View(model);

                case LoginCheckStatus.Disabled:
                    SetPopup(
                        "danger",
                        "Account disabled",
                        "Your account is disabled. Please contact PeerPulse support for assistance.",
                        "Contact support",
                        "mailto:support@instafinancials.com",
                        "Close");
                    return View(model);

                case LoginCheckStatus.VerificationRequired:
                    TempData["VerificationUserId"] = result.User!.InstaUserId;

                    SetPopup(
                        "warning",
                        "Verification required",
                        "Please verify your registered contact details before accessing PeerPulse.",
                        "Verify now",
                        Url.Action("StartVerification", "Account"),
                        "Close");
                    return View(model);

                case LoginCheckStatus.Error:
                    SetPopup(
                        "danger",
                        "Unable to sign in",
                        "Please try again in a few minutes.",
                        "Close");
                    return View(model);

                case LoginCheckStatus.Success:
                    await _subscriptionService.EnsureStarterSubscriptionAsync(
                        result.User!.InstaUserId,
                        cancellationToken);

                    var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, result.User.InstaUserId.ToString()),
                new(ClaimTypes.Name, result.User.UserName ?? result.User.UserEmail ?? "PeerPulse User"),
                new(ClaimTypes.Email, result.User.UserEmail ?? string.Empty)
            };

                    var identity = new ClaimsIdentity(claims, "PeerPulse");
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync(
                        "PeerPulse",
                        principal,
                        new AuthenticationProperties
                        {
                            IsPersistent = model.RememberMe,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                        });

                    if (!string.IsNullOrWhiteSpace(model.ReturnUrl) &&
                        Url.IsLocalUrl(model.ReturnUrl))
                    {
                        return LocalRedirect(model.ReturnUrl);
                    }

                    return RedirectToAction("Index", "Dashboard");

                default:
                    SetPopup(
                        "danger",
                        "Unable to sign in",
                        "Please try again.",
                        "Close");
                    return View(model);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"Login failed. Trace ID: {TraceId}",HttpContext.TraceIdentifier);

            model.Password = string.Empty;

            ModelState.AddModelError(string.Empty,"Login is temporarily unavailable. Please try again.");

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
    private void SetPopup(string theme,string title,string message,string primaryText,string? primaryUrl = null,string? secondaryText = null)
    {
        ViewData["PopupTheme"] = theme;
        ViewData["PopupTitle"] = title;
        ViewData["PopupMessage"] = message;
        ViewData["PopupPrimaryText"] = primaryText;
        ViewData["PopupPrimaryUrl"] = primaryUrl;
        ViewData["PopupSecondaryText"] = secondaryText;
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("PeerPulse");

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
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleResponse),
                "Account",
                new { returnUrl })
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }
    [HttpGet]
    public async Task<IActionResult> GoogleResponse(string? returnUrl, CancellationToken cancellationToken)
    {
        try
        {


            var externalResult = await HttpContext.AuthenticateAsync("PeerPulse.External");

            if (!externalResult.Succeeded || externalResult.Principal is null)
            {
                SetPopup("danger", "Google sign-in failed", "We could not complete Google sign-in. Please try again.", "Close");

                return View("Login", new LoginViewModel
                {
                    ReturnUrl = returnUrl
                });
            }

            string? googleEmail = externalResult.Principal.FindFirstValue(ClaimTypes.Email);

            // Remove the temporary Google authentication cookie.
            await HttpContext.SignOutAsync("PeerPulse.External");

            if (string.IsNullOrWhiteSpace(googleEmail))
            {
                SetPopup(
                    "warning",
                    "Google email not available",
                    "Your Google account did not provide an email address.",
                    "Close");

                return View("Login", new LoginViewModel
                {
                    ReturnUrl = returnUrl
                });
            }

            // Google email must match an existing InstaUser record.
            InstaUser? user = await _loginService.FindByEmailAsync(googleEmail.Trim(), cancellationToken);

            if (user is null)
            {
                SetPopup(
                    "warning",
                    "Account not found",
                    "This Google email is not registered with PeerPulse.",
                    "Close");

                return View("Login", new LoginViewModel
                {
                    ReturnUrl = returnUrl
                });
            }

            // Status 2: disabled. Do not sign in.
            if (user.StatusId == 2)
            {
                SetPopup(
                    "danger",
                    "Account disabled",
                    "Your account is disabled. Please contact PeerPulse support.",
                    "Contact support",
                    "mailto:support@instafinancials.com",
                    "Close");

                return View("Login", new LoginViewModel
                {
                    ReturnUrl = returnUrl
                });
            }

            // Status 0 / inactive: user must complete verification first.
            if (user.StatusId != 1 || user.IsUserActive != true)
            {
                TempData["VerificationUserId"] = user.InstaUserId;

                SetPopup(
                    "warning",
                    "Verification required",
                    "Verify your registered contact details before accessing PeerPulse.",
                    "Close");

                return View("Login", new LoginViewModel
                {
                    ReturnUrl = returnUrl
                });
            }

            // Create Starter subscription only if no subscription history exists.
            await _subscriptionService.EnsureStarterSubscriptionAsync(user.InstaUserId, cancellationToken);

            var claims = new List<Claim>
           {
               new(ClaimTypes.NameIdentifier, user.InstaUserId.ToString()),
               new(ClaimTypes.Name, user.UserName ?? user.UserEmail ?? "PeerPulse User"),
               new(ClaimTypes.Email, user.UserEmail ?? googleEmail)
           };

            var identity = new ClaimsIdentity(claims, "PeerPulse");

            await HttpContext.SignInAsync(
                "PeerPulse",
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
                });

            // Use Home until you create DashboardController.
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google login failed. Trace ID: {TraceId}", HttpContext.TraceIdentifier);
            SetPopup(
                "danger",
                "Google sign-in failed",
                "We could not complete Google sign-in. Please try again.",
                "Close");
            return View("Login", new LoginViewModel
            {
                ReturnUrl = returnUrl
            });
        }
    }
}