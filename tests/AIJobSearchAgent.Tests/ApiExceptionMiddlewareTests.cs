using System.Text.Json;
using AIJobSearchAgent.Api.Middleware;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIJobSearchAgent.Tests;

public sealed class ApiExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_MapsCandidateProfileFailureTo500()
    {
        var problem = await InvokeAsync(new CandidateProfileException("Invalid profile."));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("Candidate profile unavailable", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_MapsMatchingFailureTo502()
    {
        var exception = new JobMatchingException(
            "Malformed output.",
            "qwen3:1.7b",
            "job-match-v1",
            EvaluationStatus.Failed);

        var problem = await InvokeAsync(exception);

        Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
        Assert.Equal("Local LLM evaluation failed", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_MapsMatchingTimeoutTo504()
    {
        var exception = new JobMatchingException(
            "Ollama timed out.",
            "qwen3:1.7b",
            "job-match-v1",
            EvaluationStatus.TimedOut);

        var problem = await InvokeAsync(exception);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.Status);
        Assert.Equal("Local LLM evaluation failed", problem.Title);
    }

    private static async Task<ProblemDetails> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/jobs/test/evaluate";
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(
            _ => throw exception,
            NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Assert.IsType<ProblemDetails>(problem);
    }
}
