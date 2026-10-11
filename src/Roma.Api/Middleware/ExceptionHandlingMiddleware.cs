using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Roma.Domain.Exceptions;

namespace Roma.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = exception switch
        {
            EntityNotFoundException => HttpStatusCode.NotFound,
            DomainConflictException => HttpStatusCode.Conflict,
            DomainValidationException => HttpStatusCode.BadRequest,
            ArgumentException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        var message = (statusCode == HttpStatusCode.InternalServerError && !_env.IsDevelopment())
            ? "An unexpected error occurred."
            : exception.Message;

        var response = new
        {
            statusCode = (int)statusCode,
            message
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
