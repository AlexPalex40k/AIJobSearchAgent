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
            profile = JsonSerializer.Deserialize<CandidateProfile>(json, SerializerOptions);
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
                if (skill.YearsExperience is < 0 or > 80)
                {
                    errors.Add($"yearsExperience for skill '{skill.Name}' must be between 0 and 80");
                }

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
            ValidateRequiredText(language.Proficiency, "spokenLanguages.proficiency", 100, errors);
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
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return serializerOptions;
    }
}
