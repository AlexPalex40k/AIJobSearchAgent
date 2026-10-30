using System.Text.Json;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.AspNetCore.Mvc;

namespace AIJobSearchAgent.Api.Middleware;

public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (JobProviderException exception)
        {
            logger.LogWarning(exception, "External job provider request failed");
            await WriteProblemAsync(
                context,
                StatusCodes.Status502BadGateway,
                "Job provider unavailable",
                exception.Message);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("An operation timed out while processing {Path}", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status504GatewayTimeout,
                "Operation timed out",
                "The external operation did not complete within the configured timeout.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled error while processing {Path}", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Unexpected server error",
                "An unexpected error occurred while processing the request.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            cancellationToken: context.RequestAborted);
    }
}
