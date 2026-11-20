using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using StateBallot.Staging;

namespace StateBallot.Api;

/// <summary>
/// Sign-in for the run console. A signed-in user holds an HttpOnly cookie, and every other endpoint
/// answers 401 without one (the fallback policy). Users come from the Users table (UserStore), made
/// with the CLI's --add-user. Every signed-in user can do everything.
/// </summary>
public static class AuthApi
{
    private const string LoginLimit = "login";

    /// <summary>The signed-in user's name, recorded on the runs they start and the URLs they edit.</summary>
    public static string UserName(this ClaimsPrincipal user) => user.Identity?.Name ?? "";

    public static void AddConsoleAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "roster-console";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // The console shows its own sign-in form, so answer with a status instead of redirecting to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Ten sign-in attempts a minute per address, on top of the per-user lockout in UserStore.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    public static void MapAuthApi(this RouteGroupBuilder api, UserStore users)
    {
        api.MapPost("/login", async (LoginBody body, HttpContext context, CancellationToken ct) =>
        {
            var (result, userName) = await users.SignInAsync(body.UserName, body.Password, ct);
            if (result != UserStore.SignIn.Ok)
                return Results.Json(new
                {
                    error = result == UserStore.SignIn.Locked
                        ? $"Too many wrong passwords. Try again in {UserStore.LockoutMinutes} minutes."
                        : "The user name or password is wrong.",
                }, statusCode: StatusCodes.Status401Unauthorized);

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userName!)], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(new ClaimsPrincipal(identity));
            return Results.Ok(new { userName });
        }).AllowAnonymous().RequireRateLimiting(LoginLimit);

        api.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        }).AllowAnonymous();

        api.MapGet("/me", (ClaimsPrincipal user) => new { userName = user.UserName() });
    }

    internal sealed record LoginBody(string? UserName, string? Password);
}
