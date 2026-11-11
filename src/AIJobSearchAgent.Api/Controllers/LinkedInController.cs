using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIJobSearchAgent.Api.Controllers;

[ApiController]
[Route("api/linkedin")]
public sealed class LinkedInController(
    ILinkedInJobProvider provider,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("search")]
    [ProducesResponseType<LinkedInSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LinkedInSearchResponse>> Search(
        [FromBody] LinkedInSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var jobTitle = request.JobTitle?.Trim();
        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            ModelState.AddModelError(nameof(request.JobTitle), "Job title is required.");
        }
        else if (jobTitle.Length > 100)
        {
            ModelState.AddModelError(nameof(request.JobTitle), "Job title must not exceed 100 characters.");
        }

        var location = request.Location?.Trim();
        if (string.IsNullOrWhiteSpace(location))
        {
            ModelState.AddModelError(nameof(request.Location), "Location is required.");
        }
        else if (location.Length > 100)
        {
            ModelState.AddModelError(nameof(request.Location), "Location must not exceed 100 characters.");
        }

        if (request.MaxJobs is < 1 or > 10)
        {
            ModelState.AddModelError(nameof(request.MaxJobs), "Maximum jobs must be between 1 and 10.");
        }

        if (request.PublishedWithinDays is not (null or 1 or 7))
        {
            ModelState.AddModelError(
                nameof(request.PublishedWithinDays),
                "Publication period must be 1 day, 7 days, or omitted.");
        }

        var geoId = request.GeoId?.Trim();
        if (geoId is not null && (geoId.Length == 0 || geoId.Length > 20 || !geoId.All(char.IsDigit)))
        {
            ModelState.AddModelError(nameof(request.GeoId), "LinkedIn geoId must contain only digits.");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        var result = await provider.SearchWithDiagnosticsAsync(
            new LinkedInJobSearchCriteria(
                jobTitle!,
                location!,
                request.PublishedWithinDays,
                request.MaxJobs,
                geoId),
            cancellationToken);
        return Ok(LinkedInSearchResponse.FromResult(result));
    }
}
