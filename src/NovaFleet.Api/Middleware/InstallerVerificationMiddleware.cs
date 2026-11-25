namespace NovaFleet.Api.Middleware;

public sealed class InstallerVerificationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/health")
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "120";
        await context.Response.WriteAsync("NovaFleet upgrade verification in progress.");
    }
}
