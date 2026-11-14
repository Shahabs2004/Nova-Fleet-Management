using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NovaFleet.Api.Middleware;
using NovaFleet.Application.Common;
using Xunit;

namespace NovaFleet.IntegrationTests;

/// <summary>خطای اعتبارسنجی دامنه باید ۴۰۰ با پیام فارسی باشد، نه ۵۰۰ عمومی.</summary>
public sealed class ApiExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task Domain_argument_exception_becomes_400_without_parameter_suffix()
    {
        var (status, message) = await InvokeAsync(new ArgumentException("شماره موبایل باید ۱۱ رقم و با صفر شروع شود.", "value"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("شماره موبایل باید ۱۱ رقم و با صفر شروع شود.", message);
    }

    [Fact]
    public async Task Argument_out_of_range_becomes_400()
    {
        var (status, message) = await InvokeAsync(new ArgumentOutOfRangeException("name", "نام مکان حداکثر ۲۰۰ نویسه است."));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("نام مکان حداکثر ۲۰۰ نویسه است.", message);
    }

    [Fact]
    public async Task Validation_exception_keeps_400()
    {
        var (status, message) = await InvokeAsync(new ValidationAppException("رمز مدیر محلی باید حداقل ۸ نویسه باشد."));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("رمز مدیر محلی باید حداقل ۸ نویسه باشد.", message);
    }

    [Fact]
    public async Task Unknown_exception_stays_generic_500()
    {
        var (status, message) = await InvokeAsync(new InvalidOperationException("boom"));
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("خطای غیرمنتظره‌ای رخ داد. اگر مشکل تکرار شد با پشتیبان سامانه تماس بگیرید.", message);
    }

    private static async Task<(int Status, string Message)> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionHandlerMiddleware(_ => throw exception, NullLogger<ApiExceptionHandlerMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.GetProperty("message").GetString()!);
    }
}