using System.Diagnostics;
using System.Security.Claims;
using Backend.Modules.ApiLog;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Backend.Shared.Middleware;

public class ApiLogMiddleware(RequestDelegate next, ILogger<ApiLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        context.Request.EnableBuffering();

        var requestPayload = await ReadRequestBodyAsync(context.Request);
        var originalBody = context.Response.Body;

        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        Exception? capturedException = null;
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            capturedException = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            responseBuffer.Position = 0;
            var responsePayload = await new StreamReader(responseBuffer).ReadToEndAsync();
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            try
            {
                await WriteLogAsync(
                    scopeFactory,
                    context,
                    requestPayload,
                    responsePayload,
                    context.Response.StatusCode,
                    sw.ElapsedMilliseconds,
                    capturedException?.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write API log");
            }
        }
    }

    private static bool ShouldSkip(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/meta/callback", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0) return null;
        if (!request.Body.CanRead) return null;

        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return PayloadSanitizer.Sanitize(body);
    }

    private static async Task WriteLogAsync(
        IServiceScopeFactory scopeFactory,
        HttpContext context,
        string? requestPayload,
        string? responsePayload,
        int statusCode,
        long timelineMs,
        string? errorMessage)
    {
        var endpoint = context.Request.Path + context.Request.QueryString;
        var (controller, action) = ResolveEndpoint(context);

        Guid? userId = null;
        string? userName = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var idClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(idClaim, out var parsedId)) userId = parsedId;
            userName = context.User.Identity?.Name
                ?? context.User.FindFirstValue(ClaimTypes.Name);
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ApiLogRepository>();

        var log = new ApiLogModel
        {
            Endpoint = endpoint,
            Controller = controller,
            Action = action,
            HttpMethod = context.Request.Method,
            RequestPayload = requestPayload,
            ResponsePayload = PayloadSanitizer.Sanitize(responsePayload),
            ResponseStatus = statusCode,
            TimelineMs = timelineMs,
            CallByUserId = userId,
            CallByUserName = userName,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            ErrorMessage = errorMessage
        };

        await repo.WriteAsync(log);
    }

    private static (string Controller, string Action) ResolveEndpoint(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor)
            return (descriptor.ControllerName, descriptor.ActionName);

        return ("Unknown", context.Request.Method);
    }
}
