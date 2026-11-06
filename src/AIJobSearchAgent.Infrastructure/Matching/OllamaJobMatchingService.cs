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
    ILogger<OllamaJobMatchingService> logger) : IJobMatchingService
{
    private static readonly JsonElement ResponseSchema = CreateResponseSchema();
    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StructuredOutputSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<JobMatchResult> EvaluateAsync(
        JobVacancy vacancy,
        CandidateProfile profile,
        CancellationToken cancellationToken)
    {
        var configuredModel = options.Value.Model;
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
                    JobMatchingPrompt.CreateUserMessage(
                        vacancy,
                        profile,
                        options.Value.MaxDescriptionCharacters))
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

            OllamaMatchOutput? output;
            try
            {
                output = JsonSerializer.Deserialize<OllamaMatchOutput>(
                    ollamaResponse.Message.Content,
                    StructuredOutputSerializerOptions);
            }
            catch (JsonException exception)
            {
                throw CreateFailure("Ollama returned malformed structured output.", configuredModel, exception);
            }

            ValidateOutput(output, configuredModel);
            var knownSkills = profile.TechnicalSkills
                .Where(skill => !string.IsNullOrWhiteSpace(skill.Name))
                .GroupBy(skill => skill.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name.Trim(), StringComparer.OrdinalIgnoreCase);
            var explanation = output!.Explanation!.Trim();
            if (string.IsNullOrWhiteSpace(vacancy.Description) &&
                !explanation.StartsWith("Insufficient information:", StringComparison.OrdinalIgnoreCase))
            {
                explanation = $"Insufficient information: the vacancy description is empty. {explanation}";
            }

            return new JobMatchResult(
                output.Rating,
                NormalizeSkills(output.MatchedSkills!)
                    .Where(knownSkills.ContainsKey)
                    .Select(skill => knownSkills[skill])
                    .ToArray(),
                NormalizeSkills(output.MissingRequiredSkills!)
                    .Where(skill => !knownSkills.ContainsKey(skill))
                    .ToArray(),
                NormalizeSkills(output.MissingOptionalSkills!)
                    .Where(skill => !knownSkills.ContainsKey(skill))
                    .ToArray(),
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

    private static void ValidateOutput(OllamaMatchOutput? output, string model)
    {
        if (output is null ||
            output.Rating is < 1 or > 5 ||
            output.MatchedSkills is null ||
            output.MissingRequiredSkills is null ||
            output.MissingOptionalSkills is null ||
            string.IsNullOrWhiteSpace(output.Explanation) ||
            output.Explanation.Length > 10_000 ||
            HasInvalidSkillList(output.MatchedSkills) ||
            HasInvalidSkillList(output.MissingRequiredSkills) ||
            HasInvalidSkillList(output.MissingOptionalSkills))
        {
            throw CreateFailure(
                "Ollama structured output failed validation. Rating must be 1-5 and all required fields must be present.",
                model);
        }
    }

    private static bool HasInvalidSkillList(IReadOnlyCollection<string?> skills) =>
        skills.Count > 100 || skills.Any(skill => string.IsNullOrWhiteSpace(skill) || skill.Length > 100);

    private static IReadOnlyList<string> NormalizeSkills(IEnumerable<string?> skills) => skills
        .Where(skill => !string.IsNullOrWhiteSpace(skill))
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
            "rating": { "type": "integer", "minimum": 1, "maximum": 5 },
            "matchedSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "missingRequiredSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "missingOptionalSkills": { "type": "array", "maxItems": 100, "items": { "type": "string", "minLength": 1, "maxLength": 100 } },
            "explanation": { "type": "string", "minLength": 1, "maxLength": 10000 }
          },
          "required": [
            "rating",
            "matchedSkills",
            "missingRequiredSkills",
            "missingOptionalSkills",
            "explanation"
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

    private sealed class OllamaMatchOutput
    {
        [JsonRequired]
        [JsonPropertyName("rating")]
        public int Rating { get; init; }

        [JsonRequired]
        [JsonPropertyName("matchedSkills")]
        public List<string?>? MatchedSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("missingRequiredSkills")]
        public List<string?>? MissingRequiredSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("missingOptionalSkills")]
        public List<string?>? MissingOptionalSkills { get; init; }

        [JsonRequired]
        [JsonPropertyName("explanation")]
        public string? Explanation { get; init; }
    }
}
