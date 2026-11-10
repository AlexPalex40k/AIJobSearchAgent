using System.ComponentModel.DataAnnotations;
using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace AIJobSearchAgent.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(
    ImportJobsUseCase importJobs,
    GetJobsUseCase getJobs,
    GetRankedJobsUseCase getRankedJobs,
    EvaluateJobUseCase evaluateJob,
    EvaluateJobsBatchUseCase evaluateJobsBatch,
    GetJobEvaluationsUseCase getEvaluations) : ControllerBase
{
    [HttpPost("import")]
    [ProducesResponseType<ImportSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ImportSummary>> Import(
        [FromBody] ImportJobsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Keywords.Count > 20 || request.Keywords.Any(keyword => keyword?.Length > 100))
        {
            ModelState.AddModelError(
                nameof(request.Keywords),
                "At most 20 keywords are allowed, and each keyword must be at most 100 characters.");
            return ValidationProblem(ModelState);
        }

        var criteria = new JobSearchCriteria(request.Keywords, request.MaxPages);
        return Ok(await importJobs.ExecuteAsync(criteria, cancellationToken));
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<JobVacancyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<JobVacancyDto>>> Get(
        [FromQuery, StringLength(KeywordMatcher.MaximumSearchQueryLength)] string? keyword,
        [FromQuery, StringLength(100)] string? source,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (KeywordMatcher.TokenizeSearchQuery(keyword).Count > KeywordMatcher.MaximumSearchTokenCount)
        {
            ModelState.AddModelError(
                nameof(keyword),
                $"Search query must contain at most {KeywordMatcher.MaximumSearchTokenCount} tokens.");
            return ValidationProblem(ModelState);
        }

        var query = new JobQuery(keyword, source, page, pageSize);
        return Ok(await getJobs.ExecuteAsync(query, cancellationToken));
    }

    [HttpGet("ranked")]
    [ProducesResponseType<PagedResult<RankedJobVacancyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<RankedJobVacancyDto>>> GetRanked(
        [FromQuery, StringLength(KeywordMatcher.MaximumSearchQueryLength)] string? keyword,
        [FromQuery, StringLength(GetRankedJobsUseCase.MaximumSourceLength)] string? source,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, GetRankedJobsUseCase.MaximumPageSize)] int pageSize = 20,
        [FromQuery, StringLength(20)] string sort = RankedJobSortParser.DefaultValue,
        CancellationToken cancellationToken = default)
    {
        if (KeywordMatcher.TokenizeSearchQuery(keyword).Count > KeywordMatcher.MaximumSearchTokenCount)
        {
            ModelState.AddModelError(
                nameof(keyword),
                $"Search query must contain at most {KeywordMatcher.MaximumSearchTokenCount} tokens.");
        }

        if (!RankedJobSortParser.TryParse(sort, out var parsedSort))
        {
            ModelState.AddModelError(
                nameof(sort),
                "Sort must be rating_desc, rating_asc, or newest.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var query = new RankedJobQuery(keyword, source, page, pageSize, parsedSort);
        return Ok(await getRankedJobs.ExecuteAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<JobVacancyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobVacancyDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var vacancy = await getJobs.GetByIdAsync(id, cancellationToken);
        return vacancy is null ? NotFound() : Ok(vacancy);
    }

    [HttpPost("{id:guid}/evaluate")]
    [ProducesResponseType<JobMatchEvaluationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<JobMatchEvaluationDto>> Evaluate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var evaluation = await evaluateJob.ExecuteAsync(id, cancellationToken);
        return evaluation is null ? NotFound() : Ok(evaluation);
    }

    [HttpPost("evaluate-batch")]
    [ProducesResponseType<BatchJobEvaluationSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchJobEvaluationSummary>> EvaluateBatch(
        [FromBody] EvaluateJobsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.JobIds is not { Count: >= 1 and <= EvaluateJobsBatchUseCase.MaximumBatchSize } jobIds)
        {
            ModelState.AddModelError(
                nameof(request.JobIds),
                $"Between 1 and {EvaluateJobsBatchUseCase.MaximumBatchSize} vacancy IDs are required.");
            return ValidationProblem(ModelState);
        }

        return Ok(await evaluateJobsBatch.ExecuteAsync(jobIds, cancellationToken));
    }

    [HttpGet("{id:guid}/evaluations")]
    [ProducesResponseType<IReadOnlyList<JobMatchEvaluationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<JobMatchEvaluationDto>>> GetEvaluations(
        Guid id,
        CancellationToken cancellationToken)
    {
        var evaluations = await getEvaluations.ExecuteAsync(id, cancellationToken);
        return evaluations is null ? NotFound() : Ok(evaluations);
    }
}
