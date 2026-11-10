using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure.Matching;

public sealed class OllamaJobMatchingService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    JobMatchCalculator calculator,
    ILogger<OllamaJobMatchingService> logger) : IJobMatchingService
{
    private static readonly JsonElement ResponseSchema = CreateResponseSchema();
    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StructuredOutputSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly HashSet<string> SupportedLanguageLevels =
        new(["A1", "A2", "B1", "B2", "C1", "C2", "native"], StringComparer.OrdinalIgnoreCase);

    public async Task<JobMatchResult> EvaluateAsync(
        JobVacancy vacancy,
        CandidateProfile profile,
        CancellationToken cancellationToken)
    {
        var configuredModel = options.Value.Model;
        var suppliedDescription = JobMatchingPrompt.GetBoundedDescription(
            vacancy,
            options.Value.MaxDescriptionCharacters);
        var request = new OllamaChatRequest
        {
            Model = configuredModel,
            Stream = false,
            Think = false,
            Format = ResponseSchema,
            Options = new OllamaRuntimeOptions { Temperature = 0 },
            Messages =
            [
                new OllamaMessage("system", JobMatchingPrompt.SystemInstructions),
                new OllamaMessage(
                    "user",
                    JobMatchingPrompt.CreateUserMessage(vacancy, options.Value.MaxDescriptionCharacters))
            ]
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama returned HTTP status {StatusCode}", (int)response.StatusCode);
                throw CreateFailure($"Ollama returned HTTP {(int)response.StatusCode}.", configuredModel);
            }

            var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                EnvelopeSerializerOptions,
                cancellationToken);
            if (ollamaResponse is null ||
                !ollamaResponse.Done ||
                string.IsNullOrWhiteSpace(ollamaResponse.Message?.Content))
            {
                throw CreateFailure("Ollama returned an incomplete response.", configuredModel);
            }

            OllamaRequirementsOutput? output;
            try
            {
                output = JsonSerializer.Deserialize<OllamaRequirementsOutput>(
                    ollamaResponse.Message.Content,
                    StructuredOutputSerializerOptions);
            }
            catch (JsonException exception)
            {
                throw CreateFailure("Ollama returned malformed structured output.", configuredModel, exception);
            }

            ValidateOutput(output, configuredModel);
            var requirements = new ExtractedJobRequirements(
                NormalizeSkills(output!.RequiredSkills!),
                NormalizeSkills(output.OptionalSkills!),
                output.HasClearRequirements,
                output.MinimumYearsProfessionalExperience,
                output.RequiredLanguages!
                    .Select(language => new RequiredLanguage(language!.Language!.Trim(), language.MinimumLevel?.Trim()))
                    .ToArray())
            {
                CoreRequiredSkills = NormalizeSkills(output.CoreRequiredSkills!)
            };
            requirements = requirements with
            {
                RequiredLanguages = LanguageRequirementVerifier.Verify(
                    requirements.RequiredLanguages,
                    suppliedDescription)
            };
            var verification = TechnicalRequirementVerifier.Verify(requirements, suppliedDescription);
            var calculation = calculator.Calculate(verification.VerifiedRequirements, profile);
            var transparencyExplanation = TechnicalRequirementVerifier.CreateTransparencyExplanation(verification);
            var explanation = transparencyExplanation is null
                ? calculation.Explanation
                : $"{calculation.Explanation} {transparencyExplanation}";

            return new JobMatchResult(
                calculation.Rating,
                calculation.MatchedSkills,
                calculation.MissingRequiredSkills,
                calculation.MissingOptionalSkills,
                explanation,
                string.IsNullOrWhiteSpace(ollamaResponse.Model) ? configuredModel : ollamaResponse.Model,
                JobMatchingPrompt.Version,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning("Ollama evaluation timed out for model {Model}", configuredModel);
            throw new JobMatchingException(
                "Ollama evaluation timed out.",
                configuredModel,
                JobMatchingPrompt.Version,
                EvaluationStatus.TimedOut,
                exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Ollama could not be reached at the configured endpoint");
            throw CreateFailure("Ollama could not be reached.", configuredModel, exception);
        }
        catch (JsonException exception)
        {
            throw CreateFailure("Ollama returned malformed JSON.", configuredModel, exception);
        }
    }

    private static void ValidateOutput(OllamaRequirementsOutput? output, string model)
    {
        if (output is null ||
            output.RequiredSkills is null ||
            output.CoreRequiredSkills is null ||
            output.OptionalSkills is null ||
            output.RequiredLanguages is null ||
            output.MinimumYearsProfessionalExperience is < 0 or > 100 ||
            HasInvalidSkillList(output.RequiredSkills) ||
            HasInvalidSkillList(output.CoreRequiredSkills) ||
            HasInvalidSkillList(output.OptionalSkills) ||
            output.RequiredLanguages.Count > 20 ||
            output.RequiredLanguages.Any(IsInvalidLanguage))
        {
            throw CreateFailure(
                "Ollama structured output failed validation. All required fields must be present and valid.",
                model);
        }
    }

    private static bool HasInvalidSkillList(IReadOnlyCollection<string?> skills) =>
        skills.Count > 100 || skills.Any(skill => string.IsNullOrWhiteSpace(skill) || skill.Length > 100);

    private static bool IsInvalidLanguage(OllamaRequiredLanguage? language) =>
        language is null ||
        string.IsNullOrWhiteSpace(language.Language) ||
        language.Language.Length > 100 ||
        language.MinimumLevel is { Length: > 20 } ||
        language.MinimumLevel is not null && !SupportedLanguageLevels.Contains(language.MinimumLevel.Trim());

    private static IReadOnlyList<string> NormalizeSkills(IEnumerable<string?> skills) => skills
        .Select(skill => skill!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static JobMatchingException CreateFailure(
        string message,
        string model,
        Exception? innerException = null) => new(
            message,
            model,
            JobMatchingPrompt.Version,
            EvaluationStatus.Failed,
            innerException);

    private static JsonElement CreateResponseSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "requiredSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "coreRequiredSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "optionalSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "hasClearRequirements": { "type": "boolean" },
            "minimumYearsProfessionalExperience": { "type": ["number", "null"], "minimum": 0, "maximum": 100 },
            "requiredLanguages": {
              "type": "array",
              "maxItems": 20,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "language": { "type": "string", "minLength": 1, "maxLength": 100 },
                  "minimumLevel": { "type": ["string", "null"], "enum": ["A1", "A2", "B1", "B2", "C1", "C2", "native", null] }
                },
                "required": ["language", "minimumLevel"]
              }
            }
          },
          "required": [
            "requiredSkills",
            "coreRequiredSkills",
            "optionalSkills",
            "hasClearRequirements",
            "minimumYearsProfessionalExperience",
            "requiredLanguages"
          ]
        }
        """).RootElement.Clone();

    private sealed class OllamaChatRequest
    {
        public required string Model { get; init; }
        public required IReadOnlyList<OllamaMessage> Messages { get; init; }
        public bool Stream { get; init; }
        public bool Think { get; init; }
        public JsonElement Format { get; init; }
        public required OllamaRuntimeOptions Options { get; init; }
    }

    private sealed record OllamaMessage(string Role, string Content);

    private sealed class OllamaRuntimeOptions
    {
        public int Temperature { get; init; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("message")]
        public OllamaResponseMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }
    }

    private sealed class OllamaResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class OllamaRequirementsOutput
    {
        [JsonRequired]
        [JsonPropertyName("requiredSkills")]
        public List<string?>? RequiredSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("coreRequiredSkills")]
        public List<string?>? CoreRequiredSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("optionalSkills")]
        public List<string?>? OptionalSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("hasClearRequirements")]
        public bool HasClearRequirements { get; init; }

        [JsonRequired]
        [JsonPropertyName("minimumYearsProfessionalExperience")]
        public decimal? MinimumYearsProfessionalExperience { get; init; }

        [JsonRequired]
        [JsonPropertyName("requiredLanguages")]
        public List<OllamaRequiredLanguage?>? RequiredLanguages { get; init; }
    }

    private sealed class OllamaRequiredLanguage
    {
        [JsonRequired]
        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonRequired]
        [JsonPropertyName("minimumLevel")]
        public string? MinimumLevel { get; init; }
    }
}
