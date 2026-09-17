using Microsoft.EntityFrameworkCore;
using PeerPulse.Web.Data;
using PeerPulse.Web.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

var instaUsersConnection = builder.Configuration
    .GetConnectionString("InstaUsersConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'InstaUsersConnection' not found.");

builder.Services.AddDbContext<InstaUsersDbContext>(options =>
    options.UseSqlServer(instaUsersConnection));

var peerPulseConnection = builder.Configuration
    .GetConnectionString("PeerPulseConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'PeerPulseConnection' not found.");

builder.Services.AddDbContext<PeerPulseDbContext>(options =>options.UseSqlServer(peerPulseConnection));

builder.Services.AddControllersWithViews();

builder.Services.AddScoped<InstaUserLoginRepository>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<SubscriptionService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", httpContext =>
    {
        string clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
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
const string PeerPulseScheme = "PeerPulse";
const string ExternalScheme = "PeerPulse.External";

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

        // __Host- cookies must use HTTPS, Path=/ and no Domain setting.
        options.Cookie.Name = "__Host-PeerPulse.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
        options.SlidingExpiration = true;
    })
    .AddCookie(ExternalScheme, options =>
    {
        // Short-lived cookie used only during Google OAuth.
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
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException(
                "Authentication:Google:ClientId is missing from User Secrets.");

        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Authentication:Google:ClientSecret is missing from User Secrets.");

        options.SignInScheme = ExternalScheme;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/Identity/Account/Login", () =>
    Results.Redirect("/Account/Login"));

app.Run();