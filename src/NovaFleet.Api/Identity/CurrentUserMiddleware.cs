using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NovaFleet.Api.Identity;
using NovaFleet.Application.Abstractions;
using NovaFleet.Application.Sessions;
using NovaFleet.Application.Settings;

namespace NovaFleet.Api.Identity;

/// <summary>اعتبارسنجی نشست در هر درخواست با سیاست عمر مطلق و بی‌کاری؛ نشست نامعتبر بلافاصله خاتمه می‌یابد.</summary>
public sealed class SessionCookieEvents(IServiceScopeFactory scopeFactory) : CookieAuthenticationEvents
{
    public const string SessionKeyClaim = "nf-session";

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionKey = context.Principal?.FindFirst(SessionKeyClaim)?.Value;
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            context.RejectPrincipal();
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var absolute = Parse(await settings.GetValueAsync(SettingKeys.SessionAbsoluteMinutes), SettingKeys.DefaultSessionAbsoluteMinutes);
        var idle = Parse(await settings.GetValueAsync(SettingKeys.SessionIdleMinutes), SettingKeys.DefaultSessionIdleMinutes);
        var valid = await sessions.ValidateAsync(sessionKey, absolute, idle);
        if (!valid)
        {
            context.RejectPrincipal();
        }
    }

    private static int Parse(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new { message = "برای انجام این عملیات باید وارد سامانه شوید." });
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { message = "برای انجام این عملیات مجوز ندارید." });
    }
}

/// <summary>میان‌افزار ساخت هویت درخواست از کوکی احراز هویت و عکس فوری مجوزها.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentUserHolder holder, IPermissionResolver resolver)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var username = context.User.FindFirst(ClaimTypes.Name)?.Value;
            if (!string.IsNullOrWhiteSpace(username))
            {
                var snapshot = await resolver.ResolveAsync(username);
                if (snapshot is not null && !snapshot.IsDisabled)
                {
                    holder.User = new ResolvedCurrentUser(snapshot);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { message = "نشست شما معتبر نیست؛ دوباره وارد شوید." });
                    return;
                }
            }
        }

        await next(context);
    }
}
