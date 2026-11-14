using System.Text.Json;
using NovaFleet.Application.Common;
using NovaFleet.Application.Settings;

namespace NovaFleet.Api.Middleware;

/// <summary>نگاشت استثناهای سامانه به پاسخ HTTP امن و قابل نمایش برای کاربر نهایی.</summary>
public sealed class ApiExceptionHandlerMiddleware(RequestDelegate next, ILogger<ApiExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                ValidationAppException validation => (StatusCodes.Status400BadRequest, validation.Message),
                UnauthorizedAppException unauthorized => (StatusCodes.Status401Unauthorized, unauthorized.Message),
                ForbiddenAppException forbidden => (StatusCodes.Status403Forbidden, forbidden.Message),
                NotFoundAppException notFound => (StatusCodes.Status404NotFound, notFound.Message),
                ConflictAppException conflict => (StatusCodes.Status409Conflict, conflict.Message),
                BadHttpRequestException badRequest => (StatusCodes.Status400BadRequest, "بدنه درخواست معتبر نیست."),
                ArgumentException argument => (StatusCodes.Status400BadRequest, WithoutParameterSuffix(argument.Message)),
                Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "این رکورد هم‌زمان توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه‌سازی کنید."),
                OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "درخواست لغو شد."),
                _ => (StatusCodes.Status500InternalServerError, "خطای غیرمنتظره‌ای رخ داد. اگر مشکل تکرار شد با پشتیبان سامانه تماس بگیرید.")
            };

            if (status >= 500)
            {
                logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
            }

            if (context.Response.HasStarted)
            {
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message, status }));
        }
    }

    /// <summary>پیام‌های اعتبارسنجی دامنه فارسی و خطاب به کاربرند؛ پسوند نام پارامتر فقط نویز است.</summary>
    private static string WithoutParameterSuffix(string message)
    {
        var index = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
    }
}

/// <summary>دفاع ساده و مؤثر در برابر CSRF: مرورگرها نمی‌توانند هدر سفارشی را بدون preflight بفرستند.</summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS"];

    public async Task InvokeAsync(HttpContext context)
    {
        var isApi = context.Request.Path.StartsWithSegments("/api");
        var isAuthPath = context.Request.Path.StartsWithSegments("/api/auth")
            || context.Request.Path.StartsWithSegments("/api/setup");
        if (isApi && !isAuthPath && !SafeMethods.Contains(context.Request.Method)
            && context.Request.Headers.XRequestedWith != "XMLHttpRequest")
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "درخواست از مرورگر تأیید نشد." });
            return;
        }

        await next(context);
    }
}
