using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Application.Exceptions;
using AIJobSearchAgent.Application.Models;
using AIJobSearchAgent.Domain.Profiles;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure.Profiles;

public sealed class CandidateProfileFileProvider(
    IOptions<CandidateProfileOptions> options,
    CandidateProfileFileLocation fileLocation) : ICandidateProfileProvider
{
    private static readonly IReadOnlySet<string> SupportedLanguageLevels = new HashSet<string>(
        ["A1", "A2", "B1", "B2", "C1", "C2", "native"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public async Task<CandidateProfileSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var configuredPath = options.Value.Path;
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, fileLocation.BaseDirectory);

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new CandidateProfileException(
                "Candidate profile file was not found. Copy candidate-profile.example.json to candidate-profile.json and customize it.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CandidateProfileException("Candidate profile file could not be read.", exception);
        }

        CandidateProfile? profile;
        try
        {
            RejectDeprecatedProperties(json);
            profile = JsonSerializer.Deserialize<CandidateProfile>(json, SerializerOptions);
        }
        catch (CandidateProfileException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CandidateProfileException("Candidate profile JSON is malformed.", exception);
        }

        if (profile is null)
        {
            throw new CandidateProfileException("Candidate profile JSON is empty.");
        }

        Validate(profile);
        var canonicalJson = JsonSerializer.SerializeToUtf8Bytes(profile, SerializerOptions);
        var profileHash = Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
        return new CandidateProfileSnapshot(profile, profileHash);
    }

    private static void Validate(CandidateProfile profile)
    {
        var errors = new List<string>();

        ValidateRequiredText(profile.ProfessionalTitle, "professionalTitle", 200, errors);
        if (profile.YearsProfessionalExperience is < 0 or > 80)
        {
            errors.Add("yearsProfessionalExperience must be between 0 and 80");
        }

        if (profile.TechnicalSkills is not { Count: > 0 })
        {
            errors.Add("technicalSkills must contain at least one skill");
        }
        else
        {
            foreach (var skill in profile.TechnicalSkills)
            {
                if (skill is null)
                {
                    errors.Add("technicalSkills cannot contain null values");
                    continue;
                }

                ValidateRequiredText(skill.Name, "technicalSkills.name", 100, errors);
                ValidateOptionalText(skill.Level, "technicalSkills.level", 100, errors);
            }
        }

        ValidateTextList(profile.PreferredJobTitles ?? [], "preferredJobTitles", 100, errors);
        ValidateTextList(profile.PreferredLocations ?? [], "preferredLocations", 200, errors);

        foreach (var language in profile.SpokenLanguages ?? [])
        {
            if (language is null)
            {
                errors.Add("spokenLanguages cannot contain null values");
                continue;
            }

            ValidateRequiredText(language.Language, "spokenLanguages.language", 100, errors);
            ValidateRequiredText(language.Level, "spokenLanguages.level", 100, errors);
            if (!string.IsNullOrWhiteSpace(language.Level) &&
                !SupportedLanguageLevels.Contains(language.Level))
            {
                errors.Add(
                    $"spokenLanguages.level '{language.Level}' is unsupported; use A1, A2, B1, B2, C1, C2, or native");
            }
        }

        if (!Enum.IsDefined(profile.RemoteWorkPreference))
        {
            errors.Add("remoteWorkPreference is invalid");
        }

        ValidateOptionalText(profile.Name, "name", 200, errors);
        ValidateOptionalText(
            profile.AdditionalExperienceOrPreferences,
            "additionalExperienceOrPreferences",
            5_000,
            errors);

        if (errors.Count > 0)
        {
            throw new CandidateProfileException($"Candidate profile is invalid: {string.Join("; ", errors)}.");
        }
    }

    private static void RejectDeprecatedProperties(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var errors = new List<string>();
        if (TryGetPropertyIgnoreCase(document.RootElement, "technicalSkills", out var technicalSkills) &&
            technicalSkills.ValueKind == JsonValueKind.Array &&
            technicalSkills.EnumerateArray().Any(element =>
                element.ValueKind == JsonValueKind.Object &&
                HasPropertyIgnoreCase(element, "yearsExperience")))
        {
            errors.Add("remove deprecated 'technicalSkills[].yearsExperience'");
        }

        if (TryGetPropertyIgnoreCase(document.RootElement, "spokenLanguages", out var spokenLanguages) &&
            spokenLanguages.ValueKind == JsonValueKind.Array &&
            spokenLanguages.EnumerateArray().Any(element =>
                element.ValueKind == JsonValueKind.Object &&
                HasPropertyIgnoreCase(element, "proficiency")))
        {
            errors.Add("rename deprecated 'spokenLanguages[].proficiency' to 'spokenLanguages[].level'");
        }

        if (errors.Count > 0)
        {
            throw new CandidateProfileException(
                $"Candidate profile uses the previous JSON format: {string.Join("; ", errors)}. " +
                "Supported language levels are A1, A2, B1, B2, C1, C2, and native.");
        }
    }

    private static bool HasPropertyIgnoreCase(JsonElement element, string propertyName) =>
        element.EnumerateObject().Any(property =>
            string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ValidateTextList(
        IReadOnlyList<string> values,
        string propertyName,
        int maximumLength,
        ICollection<string> errors)
    {
        foreach (var value in values)
        {
            ValidateRequiredText(value, propertyName, maximumLength, errors);
        }
    }

    private static void ValidateRequiredText(
        string? value,
        string propertyName,
        int maximumLength,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{propertyName} is required");
        }
        else if (value.Length > maximumLength)
        {
            errors.Add($"{propertyName} must not exceed {maximumLength} characters");
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string propertyName,
        int maximumLength,
        ICollection<string> errors)
    {
        if (value?.Length > maximumLength)
        {
            errors.Add($"{propertyName} must not exceed {maximumLength} characters");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return serializerOptions;
    }
}
