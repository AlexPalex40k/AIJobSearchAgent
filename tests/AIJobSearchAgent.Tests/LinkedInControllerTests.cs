using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Api.Controllers;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace AIJobSearchAgent.Tests;

public sealed class LinkedInControllerTests
{
    public static IEnumerable<object[]> InvalidRequiredTextValues()
    {
        yield return [null!, "Serbia", nameof(LinkedInSearchRequest.JobTitle)];
        yield return [string.Empty, "Serbia", nameof(LinkedInSearchRequest.JobTitle)];
        yield return ["   ", "Serbia", nameof(LinkedInSearchRequest.JobTitle)];
        yield return [new string('x', 101), "Serbia", nameof(LinkedInSearchRequest.JobTitle)];
        yield return [".NET", null!, nameof(LinkedInSearchRequest.Location)];
        yield return [".NET", string.Empty, nameof(LinkedInSearchRequest.Location)];
        yield return [".NET", "   ", nameof(LinkedInSearchRequest.Location)];
        yield return [".NET", new string('x', 101), nameof(LinkedInSearchRequest.Location)];
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredTextValues))]
    public async Task Search_rejects_invalid_required_text_without_calling_provider(
        string jobTitle,
        string location,
        string expectedField)
    {
        var provider = new StubLinkedInProvider();
        var controller = new LinkedInController(provider, new StubEnvironment("Development"));

        var result = await controller.Search(
            new LinkedInSearchRequest
            {
                JobTitle = jobTitle,
                Location = location
            },
            CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.True(controller.ModelState.ContainsKey(expectedField));
        Assert.Single(controller.ModelState[expectedField]!.Errors);
        Assert.False(provider.Called);
    }

    [Fact]
    public async Task Search_rejects_invalid_parameters_without_calling_provider()
    {
        var provider = new StubLinkedInProvider();
        var controller = new LinkedInController(provider, new StubEnvironment("Development"));

        var result = await controller.Search(
            new LinkedInSearchRequest
            {
                JobTitle = ".NET",
                Location = "Serbia",
                PublishedWithinDays = 30,
                MaxJobs = 11,
                GeoId = "invalid"
            },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(provider.Called);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Search_returns_structured_partial_result_in_development()
    {
        var vacancy = new JobVacancy
        {
            ExternalId = "4487735427",
            Source = "LinkedIn",
            Title = ".NET Engineer",
            Company = "Example",
            Location = "Serbia",
            Description = string.Empty,
            Url = "https://www.linkedin.com/jobs/view/4487735427"
        };
        var provider = new StubLinkedInProvider(new LinkedInJobSearchResult(
            [vacancy],
            IsPartial: true,
            IsBlocked: false,
            LoginRequired: true,
            RestrictionReason: "Descriptions require login.",
            Limitations: ["One description was unavailable."]));
        var controller = new LinkedInController(provider, new StubEnvironment("Development"));

        var result = await controller.Search(
            new LinkedInSearchRequest
            {
                JobTitle = ".NET",
                Location = "Serbia",
                PublishedWithinDays = 7,
                MaxJobs = 10
            },
            CancellationToken.None);

        var response = Assert.IsType<LinkedInSearchResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.IsPartial);
        Assert.True(response.LoginRequired);
        Assert.Single(response.Vacancies);
        Assert.Equal(string.Empty, response.Vacancies[0].Description);
        Assert.Equal(".NET", provider.ReceivedCriteria?.JobTitle);
    }

    [Fact]
    public async Task Search_is_not_available_outside_development()
    {
        var provider = new StubLinkedInProvider();
        var controller = new LinkedInController(provider, new StubEnvironment("Production"));

        var result = await controller.Search(
            new LinkedInSearchRequest { JobTitle = ".NET", Location = "Serbia" },
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.False(provider.Called);
    }

    private sealed class StubLinkedInProvider(
        LinkedInJobSearchResult? result = null) : ILinkedInJobProvider
    {
        public string SourceName => "LinkedIn";

        public bool Called { get; private set; }

        public LinkedInJobSearchCriteria? ReceivedCriteria { get; private set; }

        public Task<IReadOnlyList<JobVacancy>> SearchAsync(
            JobSearchCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobVacancy>>(result?.Vacancies ?? []);

        public Task<LinkedInJobSearchResult> SearchWithDiagnosticsAsync(
            LinkedInJobSearchCriteria criteria,
            CancellationToken cancellationToken)
        {
            Called = true;
            ReceivedCriteria = criteria;
            return Task.FromResult(result ?? new LinkedInJobSearchResult([], false, false, false, null, []));
        }
    }

    private sealed class StubEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AIJobSearchAgent.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
