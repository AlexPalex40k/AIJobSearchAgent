using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure.Matching;

public sealed class OllamaJobRequirementsExtractor(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaJobRequirementsExtractor> logger) : IJobRequirementsExtractor
{
    private static readonly JsonElement ResponseSchema = CreateResponseSchema();
    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StructuredOutputSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<VacancyRequirementsExtraction> ExtractAsync(
        JobVacancy vacancy,
        CancellationToken cancellationToken)
    {
        var configuredModel = options.Value.Model;
        if (string.IsNullOrWhiteSpace(vacancy.Description))
        {
            return EmptyDescriptionResult(configuredModel);
        }

        var suppliedDescription = vacancy.Description.Length <= options.Value.MaxDescriptionCharacters
            ? vacancy.Description
            : vacancy.Description[..options.Value.MaxDescriptionCharacters];
        var request = new OllamaChatRequest
        {
            Model = configuredModel,
            Stream = false,
            Think = false,
            Format = ResponseSchema,
            Options = new OllamaRuntimeOptions { Temperature = 0 },
            Messages =
            [
                new OllamaMessage("system", VacancyRequirementsExtractionPrompt.SystemInstructions),
                new OllamaMessage(
                    "user",
                    VacancyRequirementsExtractionPrompt.CreateUserMessage(
                        vacancy,
                        options.Value.MaxDescriptionCharacters))
            ]
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Ollama requirement extraction returned HTTP status {StatusCode}",
                    (int)response.StatusCode);
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
                throw CreateFailure(
                    "Ollama returned malformed structured requirement output.",
                    configuredModel,
                    exception);
            }

            ValidateOutput(output, suppliedDescription, configuredModel);
            return MapOutput(
                output!,
                string.IsNullOrWhiteSpace(ollamaResponse.Model) ? configuredModel : ollamaResponse.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning("Ollama requirement extraction timed out for model {Model}", configuredModel);
            throw new VacancyRequirementsExtractionException(
                "Ollama requirement extraction timed out.",
                configuredModel,
                VacancyRequirementsExtractionPrompt.Version,
                RequirementsExtractionFailureKind.TimedOut,
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

    private static VacancyRequirementsExtraction EmptyDescriptionResult(string model) => new(
        MandatoryTechnicalSkills: [],
        OptionalTechnicalSkills: [],
        ContextualTechnologyMentions: [],
        OverallExperienceRequirements: [],
        TechnologyExperienceRequirements: [],
        LanguageRequirements: [],
        LocationRequirements: [],
        WorkArrangementConditions: [],
        RelocationConditions: [],
        AmbiguousRequirements: [],
        IsDescriptionInsufficient: true,
        InsufficientInformation: ["The vacancy description is empty."],
        ModelName: model,
        PromptVersion: VacancyRequirementsExtractionPrompt.Version,
        ExtractedAt: DateTimeOffset.UtcNow);

    private static void ValidateOutput(
        OllamaRequirementsOutput? output,
        string suppliedDescription,
        string model)
    {
        if (output is null ||
            output.MandatoryTechnicalSkills is null ||
            output.OptionalTechnicalSkills is null ||
            output.ContextualTechnologyMentions is null ||
            output.OverallExperienceRequirements is null ||
            output.TechnologyExperienceRequirements is null ||
            output.LanguageRequirements is null ||
            output.LocationRequirements is null ||
            output.WorkArrangementConditions is null ||
            output.RelocationConditions is null ||
            output.AmbiguousRequirements is null ||
            output.InsufficientInformation is null)
        {
            throw CreateFailure("Ollama structured requirement output is missing required fields.", model);
        }

        if (HasTooManyItems(output) ||
            output.InsufficientInformation.Any(value => !IsValidText(value, 1_000)) ||
            !AllValid(output.MandatoryTechnicalSkills, item =>
                IsValidText(item.Name, 200) &&
                HasValidNamedEvidence(item.Name!, item.Evidence, suppliedDescription)) ||
            !AllValid(output.OptionalTechnicalSkills, item =>
                IsValidText(item.Name, 200) &&
                HasValidNamedEvidence(item.Name!, item.Evidence, suppliedDescription)) ||
            !AllValid(output.ContextualTechnologyMentions, item =>
                IsValidText(item.Name, 200) &&
                HasValidNamedEvidence(item.Name!, item.Evidence, suppliedDescription)) ||
            !AllValid(output.OverallExperienceRequirements, item =>
                IsValidYears(item.MinimumYears) &&
                IsValidText(item.Description, 1_000) &&
                HasValidEvidence(item.Evidence, suppliedDescription)) ||
            !AllValid(output.TechnologyExperienceRequirements, item =>
                IsValidText(item.Technology, 200) &&
                IsValidYears(item.MinimumYears) &&
                IsValidText(item.Description, 1_000) &&
                HasValidNamedEvidence(item.Technology!, item.Evidence, suppliedDescription)) ||
            !AllValid(output.LanguageRequirements, item =>
                IsValidText(item.Language, 200) &&
                IsValidOptionalText(item.ProficiencyDescription, 500) &&
                IsValidCefrLevel(item.CefrLevel) &&
                IsValidEnum<RequirementClassification>(item.Classification) &&
                HasValidNamedEvidence(item.Language!, item.Evidence, suppliedDescription) &&
                (item.CefrLevel is null || item.Evidence!.Quote!.Contains(
                    item.CefrLevel,
                    StringComparison.OrdinalIgnoreCase))) ||
            !AllValid(output.LocationRequirements, item =>
                IsValidText(item.Location, 500) &&
                IsValidEnum<RequirementClassification>(item.Classification) &&
                HasValidEvidence(item.Evidence, suppliedDescription)) ||
            !AllValid(output.WorkArrangementConditions, item =>
                IsValidEnum<WorkArrangement>(item.Arrangement) &&
                IsValidEnum<RequirementClassification>(item.Classification) &&
                IsValidOptionalText(item.Details, 1_000) &&
                HasValidEvidence(item.Evidence, suppliedDescription)) ||
            !AllValid(output.RelocationConditions, item =>
                IsValidEnum<RelocationDisposition>(item.Disposition) &&
                IsValidEnum<RequirementClassification>(item.Classification) &&
                IsValidOptionalText(item.Details, 1_000) &&
                HasValidEvidence(item.Evidence, suppliedDescription)) ||
            !AllValid(output.AmbiguousRequirements, item =>
                IsValidText(item.Subject, 200) &&
                IsValidText(item.Description, 1_000) &&
                HasValidEvidence(item.Evidence, suppliedDescription)))
        {
            throw CreateFailure(
                "Ollama structured requirement output failed validation, including evidence quotation validation.",
                model);
        }

        var classifiedTechnologies = output.MandatoryTechnicalSkills
            .Select(item => (item!.Name!, Category: 0))
            .Concat(output.OptionalTechnicalSkills.Select(item => (item!.Name!, Category: 1)))
            .Concat(output.ContextualTechnologyMentions.Select(item => (item!.Name!, Category: 2)));
        if (classifiedTechnologies
            .GroupBy(item => item.Item1.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Select(item => item.Category).Distinct().Count() > 1))
        {
            throw CreateFailure(
                "Ollama classified the same technology in contradictory requirement categories.",
                model);
        }
    }

    private static bool HasTooManyItems(OllamaRequirementsOutput output) =>
        output.MandatoryTechnicalSkills!.Count > 100 ||
        output.OptionalTechnicalSkills!.Count > 100 ||
        output.ContextualTechnologyMentions!.Count > 100 ||
        output.OverallExperienceRequirements!.Count > 100 ||
        output.TechnologyExperienceRequirements!.Count > 100 ||
        output.LanguageRequirements!.Count > 100 ||
        output.LocationRequirements!.Count > 100 ||
        output.WorkArrangementConditions!.Count > 100 ||
        output.RelocationConditions!.Count > 100 ||
        output.AmbiguousRequirements!.Count > 100 ||
        output.InsufficientInformation!.Count > 100;

    private static bool AllValid<T>(IEnumerable<T?> items, Func<T, bool> predicate) where T : class =>
        items.All(item => item is not null && predicate(item));

    private static bool HasValidEvidence(EvidenceOutput? evidence, string suppliedDescription) =>
        evidence is not null &&
        IsValidText(evidence.Quote, 2_000) &&
        suppliedDescription.Contains(evidence.Quote!, StringComparison.Ordinal);

    private static bool HasValidNamedEvidence(
        string name,
        EvidenceOutput? evidence,
        string suppliedDescription) =>
        HasValidEvidence(evidence, suppliedDescription) &&
        evidence!.Quote!.Contains(name, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool IsValidOptionalText(string? value, int maximumLength) =>
        value is not null && value.Length <= maximumLength;

    private static bool IsValidYears(int? years) => years is null or >= 0 and <= 100;

    private static bool IsValidCefrLevel(string? value) =>
        value is null || IsValidEnum<CefrLevel>(value);

    private static bool IsValidEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        value is not null &&
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed);

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(value, ignoreCase: true);

    private static VacancyRequirementsExtraction MapOutput(
        OllamaRequirementsOutput output,
        string model) => new(
        output.MandatoryTechnicalSkills!.Select(MapTechnicalRequirement).ToArray(),
        output.OptionalTechnicalSkills!.Select(MapTechnicalRequirement).ToArray(),
        output.ContextualTechnologyMentions!.Select(MapTechnicalRequirement).ToArray(),
        output.OverallExperienceRequirements!.Select(item => new OverallExperienceRequirement(
            item!.MinimumYears,
            item.Description!.Trim(),
            MapEvidence(item.Evidence!))).ToArray(),
        output.TechnologyExperienceRequirements!.Select(item => new TechnologyExperienceRequirement(
            item!.Technology!.Trim(),
            item.MinimumYears,
            item.Description!.Trim(),
            MapEvidence(item.Evidence!))).ToArray(),
        output.LanguageRequirements!.Select(item => new LanguageRequirement(
            item!.Language!.Trim(),
            item.CefrLevel is null ? null : ParseEnum<CefrLevel>(item.CefrLevel),
            item.ProficiencyDescription!.Trim(),
            ParseEnum<RequirementClassification>(item.Classification!),
            MapEvidence(item.Evidence!))).ToArray(),
        output.LocationRequirements!.Select(item => new LocationRequirement(
            item!.Location!.Trim(),
            ParseEnum<RequirementClassification>(item.Classification!),
            MapEvidence(item.Evidence!))).ToArray(),
        output.WorkArrangementConditions!.Select(item => new WorkArrangementCondition(
            ParseEnum<WorkArrangement>(item!.Arrangement!),
            ParseEnum<RequirementClassification>(item.Classification!),
            item.Details!.Trim(),
            MapEvidence(item.Evidence!))).ToArray(),
        output.RelocationConditions!.Select(item => new RelocationCondition(
            ParseEnum<RelocationDisposition>(item!.Disposition!),
            ParseEnum<RequirementClassification>(item.Classification!),
            item.Details!.Trim(),
            MapEvidence(item.Evidence!))).ToArray(),
        output.AmbiguousRequirements!.Select(item => new AmbiguousRequirement(
            item!.Subject!.Trim(),
            item.Description!.Trim(),
            MapEvidence(item.Evidence!))).ToArray(),
        output.IsDescriptionInsufficient,
        output.InsufficientInformation!.Select(value => value!.Trim()).ToArray(),
        model,
        VacancyRequirementsExtractionPrompt.Version,
        DateTimeOffset.UtcNow);

    private static TechnicalRequirement MapTechnicalRequirement(TechnicalRequirementOutput? item) => new(
        item!.Name!.Trim(),
        MapEvidence(item.Evidence!));

    private static RequirementEvidence MapEvidence(EvidenceOutput evidence) => new(evidence.Quote!);

    private static VacancyRequirementsExtractionException CreateFailure(
        string message,
        string model,
        Exception? innerException = null) => new(
        message,
        model,
        VacancyRequirementsExtractionPrompt.Version,
        RequirementsExtractionFailureKind.Failed,
        innerException);

    private static JsonElement CreateResponseSchema() => JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "mandatoryTechnicalSkills": { "$ref": "#/$defs/technicalRequirements" },
            "optionalTechnicalSkills": { "$ref": "#/$defs/technicalRequirements" },
            "contextualTechnologyMentions": { "$ref": "#/$defs/technicalRequirements" },
            "overallExperienceRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "minimumYears": { "type": ["integer", "null"], "minimum": 0, "maximum": 100 },
                  "description": { "type": "string", "minLength": 1, "maxLength": 1000 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["minimumYears", "description", "evidence"]
              }
            },
            "technologyExperienceRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "technology": { "type": "string", "minLength": 1, "maxLength": 200 },
                  "minimumYears": { "type": ["integer", "null"], "minimum": 0, "maximum": 100 },
                  "description": { "type": "string", "minLength": 1, "maxLength": 1000 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["technology", "minimumYears", "description", "evidence"]
              }
            },
            "languageRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "language": { "type": "string", "minLength": 1, "maxLength": 200 },
                  "cefrLevel": { "type": ["string", "null"], "enum": [null, "A1", "A2", "B1", "B2", "C1", "C2"] },
                  "proficiencyDescription": { "type": "string", "maxLength": 500 },
                  "classification": { "$ref": "#/$defs/classification" },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["language", "cefrLevel", "proficiencyDescription", "classification", "evidence"]
              }
            },
            "locationRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "location": { "type": "string", "minLength": 1, "maxLength": 500 },
                  "classification": { "$ref": "#/$defs/classification" },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["location", "classification", "evidence"]
              }
            },
            "workArrangementConditions": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "arrangement": { "type": "string", "enum": ["remote", "hybrid", "onSite"] },
                  "classification": { "$ref": "#/$defs/classification" },
                  "details": { "type": "string", "maxLength": 1000 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["arrangement", "classification", "details", "evidence"]
              }
            },
            "relocationConditions": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "disposition": { "type": "string", "enum": ["required", "available", "notAvailable"] },
                  "classification": { "$ref": "#/$defs/classification" },
                  "details": { "type": "string", "maxLength": 1000 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["disposition", "classification", "details", "evidence"]
              }
            },
            "ambiguousRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "subject": { "type": "string", "minLength": 1, "maxLength": 200 },
                  "description": { "type": "string", "minLength": 1, "maxLength": 1000 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["subject", "description", "evidence"]
              }
            },
            "isDescriptionInsufficient": { "type": "boolean" },
            "insufficientInformation": {
              "type": "array", "maxItems": 100,
              "items": { "type": "string", "minLength": 1, "maxLength": 1000 }
            }
          },
          "required": [
            "mandatoryTechnicalSkills", "optionalTechnicalSkills", "contextualTechnologyMentions",
            "overallExperienceRequirements", "technologyExperienceRequirements", "languageRequirements",
            "locationRequirements", "workArrangementConditions", "relocationConditions",
            "ambiguousRequirements", "isDescriptionInsufficient", "insufficientInformation"
          ],
          "$defs": {
            "evidence": {
              "type": "object", "additionalProperties": false,
              "properties": { "quote": { "type": "string", "minLength": 1, "maxLength": 2000 } },
              "required": ["quote"]
            },
            "classification": {
              "type": "string", "enum": ["mandatory", "optional", "contextual", "ambiguous"]
            },
            "technicalRequirements": {
              "type": "array", "maxItems": 100,
              "items": {
                "type": "object", "additionalProperties": false,
                "properties": {
                  "name": { "type": "string", "minLength": 1, "maxLength": 200 },
                  "evidence": { "$ref": "#/$defs/evidence" }
                },
                "required": ["name", "evidence"]
              }
            }
          }
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
        [JsonRequired, JsonPropertyName("mandatoryTechnicalSkills")]
        public List<TechnicalRequirementOutput?>? MandatoryTechnicalSkills { get; init; }

        [JsonRequired, JsonPropertyName("optionalTechnicalSkills")]
        public List<TechnicalRequirementOutput?>? OptionalTechnicalSkills { get; init; }

        [JsonRequired, JsonPropertyName("contextualTechnologyMentions")]
        public List<TechnicalRequirementOutput?>? ContextualTechnologyMentions { get; init; }

        [JsonRequired, JsonPropertyName("overallExperienceRequirements")]
        public List<OverallExperienceOutput?>? OverallExperienceRequirements { get; init; }

        [JsonRequired, JsonPropertyName("technologyExperienceRequirements")]
        public List<TechnologyExperienceOutput?>? TechnologyExperienceRequirements { get; init; }

        [JsonRequired, JsonPropertyName("languageRequirements")]
        public List<LanguageRequirementOutput?>? LanguageRequirements { get; init; }

        [JsonRequired, JsonPropertyName("locationRequirements")]
        public List<LocationRequirementOutput?>? LocationRequirements { get; init; }

        [JsonRequired, JsonPropertyName("workArrangementConditions")]
        public List<WorkArrangementOutput?>? WorkArrangementConditions { get; init; }

        [JsonRequired, JsonPropertyName("relocationConditions")]
        public List<RelocationOutput?>? RelocationConditions { get; init; }

        [JsonRequired, JsonPropertyName("ambiguousRequirements")]
        public List<AmbiguousRequirementOutput?>? AmbiguousRequirements { get; init; }

        [JsonRequired, JsonPropertyName("isDescriptionInsufficient")]
        public bool IsDescriptionInsufficient { get; init; }

        [JsonRequired, JsonPropertyName("insufficientInformation")]
        public List<string?>? InsufficientInformation { get; init; }
    }

    private sealed class EvidenceOutput
    {
        [JsonRequired, JsonPropertyName("quote")]
        public string? Quote { get; init; }
    }

    private sealed class TechnicalRequirementOutput
    {
        [JsonRequired, JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class OverallExperienceOutput
    {
        [JsonRequired, JsonPropertyName("minimumYears")]
        public int? MinimumYears { get; init; }

        [JsonRequired, JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class TechnologyExperienceOutput
    {
        [JsonRequired, JsonPropertyName("technology")]
        public string? Technology { get; init; }

        [JsonRequired, JsonPropertyName("minimumYears")]
        public int? MinimumYears { get; init; }

        [JsonRequired, JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class LanguageRequirementOutput
    {
        [JsonRequired, JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonRequired, JsonPropertyName("cefrLevel")]
        public string? CefrLevel { get; init; }

        [JsonRequired, JsonPropertyName("proficiencyDescription")]
        public string? ProficiencyDescription { get; init; }

        [JsonRequired, JsonPropertyName("classification")]
        public string? Classification { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class LocationRequirementOutput
    {
        [JsonRequired, JsonPropertyName("location")]
        public string? Location { get; init; }

        [JsonRequired, JsonPropertyName("classification")]
        public string? Classification { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class WorkArrangementOutput
    {
        [JsonRequired, JsonPropertyName("arrangement")]
        public string? Arrangement { get; init; }

        [JsonRequired, JsonPropertyName("classification")]
        public string? Classification { get; init; }

        [JsonRequired, JsonPropertyName("details")]
        public string? Details { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class RelocationOutput
    {
        [JsonRequired, JsonPropertyName("disposition")]
        public string? Disposition { get; init; }

        [JsonRequired, JsonPropertyName("classification")]
        public string? Classification { get; init; }

        [JsonRequired, JsonPropertyName("details")]
        public string? Details { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }

    private sealed class AmbiguousRequirementOutput
    {
        [JsonRequired, JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonRequired, JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonRequired, JsonPropertyName("evidence")]
        public EvidenceOutput? Evidence { get; init; }
    }
}
