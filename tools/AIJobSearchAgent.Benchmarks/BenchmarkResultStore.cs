using System.Text.Json;

namespace AIJobSearchAgent.Benchmarks;

public static class BenchmarkResultStore
{
    public static async Task<string> SaveAsync(
        BenchmarkRunResult result,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var safeModelName = string.Concat(result.ModelName.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-'));
        var filename = $"{result.ExecutedAt:yyyyMMdd-HHmmssfff}_{safeModelName}_{result.RunId:N}.json";
        var path = Path.Combine(outputDirectory, filename);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    result,
                    BenchmarkJson.SerializerOptions,
                    cancellationToken);
            }

            File.Move(temporaryPath, path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return Path.GetFullPath(path);
    }

    public static async Task<BenchmarkRunResult> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        BenchmarkRunResult? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync<BenchmarkRunResult>(
                stream,
                BenchmarkJson.SerializerOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new BenchmarkValidationException("Benchmark result JSON is malformed.", exception);
        }

        if (result is null || result.SchemaVersion != BenchmarkRunResult.CurrentSchemaVersion)
        {
            throw new BenchmarkValidationException("Benchmark result is empty or uses an unsupported schema version.");
        }

        return result;
    }
}
