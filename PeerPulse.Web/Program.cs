using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Data;
using PeerPulse.Web.Services;
using Serilog;
using Serilog.Events;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

const string PeerPulseScheme = "PeerPulse";
const string ExternalScheme = "PeerPulse.External";

builder.Logging.ClearProviders();

builder.Host.UseSerilog((context, services, logger) =>
{
    logger
        .MinimumLevel.Information()
        .MinimumLevel.Override(
            "Microsoft.AspNetCore",
            LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PeerPulse");

    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console();
    }
    else
    {
        var logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Logs");

        Directory.CreateDirectory(logDirectory);

        logger.WriteTo.File(
            path: Path.Combine(logDirectory, "peerpulse-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 5,
            shared: true,
            outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                "[{Level:u3}] {Message:lj}{NewLine}{Exception}");
    }
});

var instaUsersConnection = builder.Configuration
    .GetConnectionString("InstaUsersConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'InstaUsersConnection' is missing.");

var peerPulseConnection = builder.Configuration
    .GetConnectionString("PeerPulseConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'PeerPulseConnection' is missing.");

builder.Services.AddDbContext<InstaUsersDbContext>(options =>
    options.UseSqlServer(instaUsersConnection));

builder.Services.AddDbContext<PeerPulseDbContext>(options =>
    options.UseSqlServer(peerPulseConnection));

builder.Services
    .AddControllersWithViews(options =>
    {
        // Validate antiforgery tokens on POST, PUT, PATCH and DELETE.
        options.Filters.Add(
            new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            JsonNamingPolicy.CamelCase;
    });

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";

    options.Cookie.Name = "__Host-PeerPulse.Antiforgery";
    options.Cookie.Path = "/";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Keep this name stable across deployments.
builder.Services
    .AddDataProtection()
    .SetApplicationName("PeerPulse.Web");

builder.Services.AddScoped<
    IInstaUserLoginRepository,
    InstaUserLoginRepository>();

builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPeerSearchService, PeerSearchService>();

var trustedProxyIps = builder.Configuration
    .GetSection("Security:TrustedProxyIps")
    .Get<string[]>()
    ?? Array.Empty<string>();

var useForwardedHeaders = trustedProxyIps.Length > 0;

if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto;

        // This configuration supports one proxy hop.
        options.ForwardLimit = 1;

        foreach (var proxyIp in trustedProxyIps)
        {
            if (!IPAddress.TryParse(proxyIp, out var address))
            {
                throw new InvalidOperationException(
                    $"Invalid trusted proxy IP: '{proxyIp}'.");
            }

            options.KnownProxies.Add(address);
        }
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext =>
    {
        var clientIp = httpContext.Connection
            .RemoteIpAddress?
            .ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = PeerPulseScheme;
        options.DefaultChallengeScheme = PeerPulseScheme;
        options.DefaultSignInScheme = PeerPulseScheme;
    })
    .AddCookie(PeerPulseScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";

        options.Cookie.Name = "__Host-PeerPulse.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Default when "Keep me signed in" is unchecked:
        // five minutes without a server request.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = true;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                var request = context.Request;

                var expectsJson =
                    request.Path.StartsWithSegments("/api") ||
                    request.Headers.Accept.Any(value =>
                        value?.Contains(
                            "application/json",
                            StringComparison.OrdinalIgnoreCase) == true) ||
                    string.Equals(
                        request.Headers["X-Requested-With"],
                        "XMLHttpRequest",
                        StringComparison.OrdinalIgnoreCase);

                if (expectsJson)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status401Unauthorized;

                    context.Response.Headers["X-Auth-Redirect"] =
                        "/Account/Login";

                    return Task.CompletedTask;
                }

                // If a rejected auth cookie was sent, show the existing
                // "Session expired" popup on the login page.
                var redirectUri = context.RedirectUri;

                if (request.Cookies.ContainsKey(options.Cookie.Name!))
                {
                    redirectUri = QueryHelpers.AddQueryString(
                        redirectUri,
                        "reason",
                        "expired");
                }

                context.Response.Redirect(redirectUri);
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie(ExternalScheme, options =>
    {
        options.Cookie.Name = "__Host-PeerPulse.External";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.SlidingExpiration = false;
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        var clientId = builder.Configuration["Authentication:Google:ClientId"];

        var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Google authentication credentials are missing.");
        }

        options.ClientId = clientId;
        options.ClientSecret = clientSecret;

        options.SignInScheme = ExternalScheme;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
    });

builder.Services.AddAuthorization(options =>
{
    // Require authentication by default.
    // Add [AllowAnonymous] to pages such as Login and Home.
    options.FallbackPolicy = new AuthorizationPolicyBuilder().AddAuthenticationSchemes(PeerPulseScheme).RequireAuthenticatedUser().Build();
});

var app = builder.Build();

if (useForwardedHeaders)
{
    // Must run before HTTPS redirection and authentication.
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

// Files in wwwroot are public.
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(name: "default",pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/Identity/Account/Login", () =>Results.Redirect("/Account/Login")).AllowAnonymous();

app.Run();