using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIJobSearchAgent.Benchmarks;

public sealed class BenchmarkValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public static partial class BenchmarkDatasetValidator
{
    public static void Validate(BenchmarkDataset dataset)
    {
        var errors = new List<string>();
        ValidateRequiredText(dataset.DatasetVersion, "datasetVersion", 100, errors);
        if (dataset.MatchingVersion is not null)
        {
            ValidateRequiredText(dataset.MatchingVersion, "matchingVersion", 100, errors);
        }

        if (dataset.Cases is not { Count: > 0 })
        {
            errors.Add("cases must contain at least one benchmark case");
        }
        else if (dataset.Cases.Count > 1_000)
        {
            errors.Add("cases must not contain more than 1000 benchmark cases");
        }

        var caseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var benchmarkCase in dataset.Cases ?? [])
        {
            if (benchmarkCase is null)
            {
                errors.Add("cases cannot contain null values");
                continue;
            }

            ValidateRequiredText(benchmarkCase.Id, "cases.id", 100, errors);
            if (!string.IsNullOrWhiteSpace(benchmarkCase.Id) && !CaseIdPattern().IsMatch(benchmarkCase.Id))
            {
                errors.Add($"case ID '{benchmarkCase.Id}' must use lowercase letters, numbers, and hyphens");
            }
            else if (!caseIds.Add(benchmarkCase.Id))
            {
                errors.Add($"case ID '{benchmarkCase.Id}' is duplicated");
            }

            ValidateRequiredText(benchmarkCase.VacancyTitle, $"{benchmarkCase.Id}.vacancyTitle", 300, errors);
            ValidateRequiredText(benchmarkCase.Company, $"{benchmarkCase.Id}.company", 200, errors);
            ValidateRequiredText(benchmarkCase.Location, $"{benchmarkCase.Id}.location", 300, errors);
            if (benchmarkCase.Description is null || benchmarkCase.Description.Length > 100_000)
            {
                errors.Add($"{benchmarkCase.Id}.description must be present and no longer than 100000 characters");
            }

            ValidateOptionalUrl(benchmarkCase.VacancyUrl, $"{benchmarkCase.Id}.vacancyUrl", errors);

            ValidateRequiredText(benchmarkCase.Rationale, $"{benchmarkCase.Id}.rationale", 2_000, errors);
            ValidateLabel(benchmarkCase, errors);
            ValidateTextList(benchmarkCase.ExpectedMatchedSkills, $"{benchmarkCase.Id}.expectedMatchedSkills", errors);
            ValidateTextList(benchmarkCase.ExpectedMissingSkills, $"{benchmarkCase.Id}.expectedMissingSkills", errors);
            ValidateTextList(benchmarkCase.Tags, $"{benchmarkCase.Id}.tags", errors);
        }

        if (errors.Count > 0)
        {
            throw new BenchmarkValidationException($"Benchmark dataset is invalid: {string.Join("; ", errors)}.");
        }
    }

    private static void ValidateLabel(BenchmarkCase benchmarkCase, ICollection<string> errors)
    {
        if (!Enum.IsDefined(benchmarkCase.LabelStatus))
        {
            errors.Add($"{benchmarkCase.Id}.labelStatus is invalid");
            return;
        }

        if (benchmarkCase.ExpectedRating is not null and (< 1 or > 5))
        {
            errors.Add($"{benchmarkCase.Id}.expectedRating must be between 1 and 5");
        }

        if (benchmarkCase.LabelStatus is ReferenceLabelStatus.Provisional or ReferenceLabelStatus.Approved &&
            benchmarkCase.ExpectedRating is null)
        {
            errors.Add($"{benchmarkCase.Id}.expectedRating is required for {benchmarkCase.LabelStatus} labels");
        }

        if (benchmarkCase.LabelStatus == ReferenceLabelStatus.Unlabeled && benchmarkCase.ExpectedRating is not null)
        {
            errors.Add($"{benchmarkCase.Id}.expectedRating must be null for an unlabeled case");
        }

        if (benchmarkCase.LabelStatus == ReferenceLabelStatus.Approved)
        {
            ValidateRequiredText(benchmarkCase.ReviewedBy, $"{benchmarkCase.Id}.reviewedBy", 200, errors);
            if (benchmarkCase.ReviewedAt is null)
            {
                errors.Add($"{benchmarkCase.Id}.reviewedAt is required for an approved label");
            }
        }
    }

    private static void ValidateTextList(
        IReadOnlyList<string>? values,
        string propertyName,
        ICollection<string> errors)
    {
        if (values is null)
        {
            errors.Add($"{propertyName} cannot be null");
            return;
        }

        if (values.Count > 100)
        {
            errors.Add($"{propertyName} cannot contain more than 100 values");
        }

        foreach (var value in values)
        {
            ValidateRequiredText(value, propertyName, 200, errors);
        }
    }

    private static void ValidateOptionalUrl(
        string? value,
        string propertyName,
        ICollection<string> errors)
    {
        if (!IsValidOptionalVacancyUrl(value))
        {
            errors.Add($"{propertyName} must be an absolute HTTP or HTTPS URL no longer than 2048 characters");
        }
    }

    internal static bool IsValidOptionalVacancyUrl(string? value) =>
        value is null ||
        value.Length <= 2_048 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        uri.Scheme is "http" or "https";

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

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CaseIdPattern();
}

public static class BenchmarkDatasetLoader
{
    public static async Task<BenchmarkDatasetSnapshot> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] json;
        try
        {
            json = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BenchmarkValidationException("Benchmark dataset could not be read.", exception);
        }

        BenchmarkDataset? dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<BenchmarkDataset>(json, BenchmarkJson.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new BenchmarkValidationException("Benchmark dataset JSON is malformed.", exception);
        }

        if (dataset is null)
        {
            throw new BenchmarkValidationException("Benchmark dataset JSON is empty.");
        }

        BenchmarkDatasetValidator.Validate(dataset);
        var canonicalJson = JsonSerializer.SerializeToUtf8Bytes(
            dataset,
            BenchmarkJson.CanonicalSerializerOptions);
        var hash = Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
        return new BenchmarkDatasetSnapshot(dataset, hash);
    }
}
