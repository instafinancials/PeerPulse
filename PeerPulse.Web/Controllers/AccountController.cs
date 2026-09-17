using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Client;
using PeerPulse.Web.Models.InstaUsers;
using PeerPulse.Web.Services;
using PeerPulse.Web.ViewModels.Account;
using System.Security.Claims;

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
    public async Task<IActionResult> LoginAsync(string? returnUrl = null)
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
        var properties = new GoogleChallengeProperties
        {
            RedirectUri = Url.Action(
                nameof(GoogleResponse),
                "Account",
                new { returnUrl }),
            Prompt = "select_account"
        };
        
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet]
    public async Task<IActionResult> GoogleResponse(string? returnUrl,CancellationToken cancellationToken)
    {
        try
        {
            var externalResult = await HttpContext.AuthenticateAsync(
                "PeerPulse.External");

            try
            {
                if (!externalResult.Succeeded ||
                    externalResult.Principal is null)
                {
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

                string? googleEmail = externalResult.Principal
                    .FindFirstValue(ClaimTypes.Email);

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

                // Find existing PeerPulse/InstaUser account by Gmail address.
                InstaUser? user = await _loginService.FindByEmailAsync(
                    googleEmail.Trim(),
                    cancellationToken);

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

                // Status 2 = permanently disabled: no login and no OTP.
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

                // Status 0 / inactive user: send to your OTP verification flow later.
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

                // First successful login creates Starter plan.
                await _subscriptionService.EnsureStarterSubscriptionAsync(
                    user.InstaUserId,
                    cancellationToken);

                var claims = new List<Claim>
                   {
                       new(ClaimTypes.NameIdentifier, user.InstaUserId.ToString()),
                       new(ClaimTypes.Name, user.UserName ?? user.UserEmail ?? "PeerPulse User"),
                       new(ClaimTypes.Email, user.UserEmail ?? googleEmail),
                       new(ClaimTypes.AuthenticationMethod, "Google")
                   };

                var identity = new ClaimsIdentity(claims, "PeerPulse");

                await HttpContext.SignInAsync(
                    "PeerPulse",
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        // Program.cs controls the 15-minute inactivity expiry.
                        IsPersistent = false,
                        AllowRefresh = true
                    });

                // returnUrl can be null when Login page was opened directly.
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }
            finally
            {
                // Always remove temporary Google OAuth cookie.
                await HttpContext.SignOutAsync("PeerPulse.External");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Google login failed. Trace ID: {TraceId}",
                HttpContext.TraceIdentifier);

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