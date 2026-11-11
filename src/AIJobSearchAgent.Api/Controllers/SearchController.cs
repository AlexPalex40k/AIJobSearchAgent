using AIJobSearchAgent.Api.Contracts;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.UseCases;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.AspNetCore.Mvc;

namespace AIJobSearchAgent.Api.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(
    SearchAndEvaluateJobsUseCase searchAndEvaluateJobs,
    IWebHostEnvironment environment) : ControllerBase
{
    private static readonly HashSet<string> SupportedLanguageLevels = new(
        ["A1", "A2", "B1", "B2", "C1", "C2", "native"],
        StringComparer.OrdinalIgnoreCase);

    [HttpPost("evaluate")]
    [ProducesResponseType<SearchAndEvaluateJobsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchAndEvaluateJobsResult>> Evaluate(
        [FromBody] SearchAndEvaluateRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var jobTitle = ValidateRequiredText(
            request.JobTitle,
            nameof(request.JobTitle),
            "Job title",
            100);
        var location = ValidateRequiredText(
            request.Location,
            nameof(request.Location),
            "Location",
            100);

        if (request.PublishedWithinDays is not (null or 1 or 7))
        {
            ModelState.AddModelError(
                nameof(request.PublishedWithinDays),
                "Publication period must be 1 day, 7 days, or omitted.");
        }

        if (request.MaxJobs is < 1 or > 10)
        {
            ModelState.AddModelError(
                nameof(request.MaxJobs),
                "Maximum jobs must be between 1 and 10.");
        }

        var candidateProfile = ValidateAndMapCandidateProfile(request.CandidateProfile);

        if (!ModelState.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        var criteria = new LinkedInJobSearchCriteria(
            jobTitle!,
            location!,
            request.PublishedWithinDays,
            request.MaxJobs);
        var result = await searchAndEvaluateJobs.ExecuteAsync(
            criteria,
            candidateProfile!,
            cancellationToken);

        return Ok(result);
    }

    private CandidateProfile? ValidateAndMapCandidateProfile(
        SearchCandidateProfileRequest? request)
    {
        if (request is null)
        {
            ModelState.AddModelError(
                nameof(SearchAndEvaluateRequest.CandidateProfile),
                "Candidate profile is required.");
            return null;
        }

        var professionalTitle = ValidateRequiredText(
            request.ProfessionalTitle,
            "CandidateProfile.ProfessionalTitle",
            "Professional title",
            200);

        if (request.YearsProfessionalExperience < 0)
        {
            ModelState.AddModelError(
                "CandidateProfile.YearsProfessionalExperience",
                "Years of professional experience must not be negative.");
        }

        var skills = new List<CandidateSkill>();
        if (request.TechnicalSkills is not { Count: > 0 })
        {
            ModelState.AddModelError(
                "CandidateProfile.TechnicalSkills",
                "At least one technical skill is required.");
        }
        else
        {
            for (var index = 0; index < request.TechnicalSkills.Count; index++)
            {
                var skill = request.TechnicalSkills[index];
                var key = $"CandidateProfile.TechnicalSkills[{index}]";
                if (skill is null)
                {
                    ModelState.AddModelError(key, "Technical skill must not be null.");
                    continue;
                }

                var name = ValidateRequiredText(skill.Name, $"{key}.Name", "Skill name", 100);
                var level = skill.Level?.Trim();
                if (level?.Length > 100)
                {
                    ModelState.AddModelError(
                        $"{key}.Level",
                        "Skill level must not exceed 100 characters.");
                }

                if (!string.IsNullOrEmpty(name) && (level is null || level.Length <= 100))
                {
                    skills.Add(new CandidateSkill { Name = name, Level = EmptyToNull(level) });
                }
            }
        }

        var languages = new List<SpokenLanguage>();
        if (request.SpokenLanguages is null)
        {
            ModelState.AddModelError(
                "CandidateProfile.SpokenLanguages",
                "Spoken languages must be an array.");
        }
        else
        {
            for (var index = 0; index < request.SpokenLanguages.Count; index++)
            {
                var language = request.SpokenLanguages[index];
                var key = $"CandidateProfile.SpokenLanguages[{index}]";
                if (language is null)
                {
                    ModelState.AddModelError(key, "Spoken language must not be null.");
                    continue;
                }

                var name = ValidateRequiredText(
                    language.Language,
                    $"{key}.Language",
                    "Language",
                    100);
                var level = ValidateRequiredText(
                    language.Level,
                    $"{key}.Level",
                    "Language level",
                    100);
                if (!string.IsNullOrEmpty(level) && !SupportedLanguageLevels.Contains(level))
                {
                    ModelState.AddModelError(
                        $"{key}.Level",
                        "Language level must be A1, A2, B1, B2, C1, C2, or native.");
                }

                if (!string.IsNullOrEmpty(name) &&
                    !string.IsNullOrEmpty(level) &&
                    SupportedLanguageLevels.Contains(level))
                {
                    languages.Add(new SpokenLanguage
                    {
                        Language = name,
                        Level = NormalizeLanguageLevel(level)
                    });
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return null;
        }

        return new CandidateProfile
        {
            ProfessionalTitle = professionalTitle!,
            YearsProfessionalExperience = request.YearsProfessionalExperience,
            TechnicalSkills = skills,
            SpokenLanguages = languages
        };
    }

    private string? ValidateRequiredText(
        string? value,
        string key,
        string displayName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            ModelState.AddModelError(key, $"{displayName} is required.");
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            ModelState.AddModelError(
                key,
                $"{displayName} must not exceed {maximumLength} characters.");
            return null;
        }

        return normalized;
    }

    private static string NormalizeLanguageLevel(string level) =>
        string.Equals(level, "native", StringComparison.OrdinalIgnoreCase)
            ? "native"
            : level.ToUpperInvariant();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
