using System.ComponentModel.DataAnnotations;
using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace AIJobSearchAgent.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(
    ImportJobsUseCase importJobs,
    GetJobsUseCase getJobs) : ControllerBase
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
        [FromQuery, StringLength(100)] string? keyword,
        [FromQuery, StringLength(100)] string? source,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new JobQuery(keyword, source, page, pageSize);
        return Ok(await getJobs.ExecuteAsync(query, cancellationToken));
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
}
