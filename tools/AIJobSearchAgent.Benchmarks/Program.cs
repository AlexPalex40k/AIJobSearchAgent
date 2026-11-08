using System.Text.Json;
using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Infrastructure.Matching;
using AIJobSearchAgent.Infrastructure.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Benchmarks;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        try
        {
            var commandLine = BenchmarkCommandLine.Parse(args);
            if (commandLine.ShowHelp)
            {
                Console.WriteLine(BenchmarkCommandLine.HelpText);
                return 0;
            }

            if (commandLine.IsComparison)
            {
                return await CompareAsync(commandLine.ComparisonPaths, cancellationSource.Token);
            }

            if (commandLine.IsExtraction)
            {
                return await ExtractAsync(commandLine, cancellationSource.Token);
            }

            return await RunAsync(commandLine, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Benchmark canceled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Benchmark failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(
        BenchmarkCommandLineOptions commandLine,
        CancellationToken cancellationToken)
    {
        var datasetPath = Path.GetFullPath(commandLine.DatasetPath);
        var profilePath = Path.GetFullPath(commandLine.ProfilePath);
        var outputDirectory = Path.GetFullPath(commandLine.OutputDirectory);
        var dataset = await BenchmarkDatasetLoader.LoadAsync(datasetPath, cancellationToken);

        var profileProvider = new CandidateProfileFileProvider(
            Options.Create(new CandidateProfileOptions { Path = profilePath }),
            new CandidateProfileFileLocation(Directory.GetCurrentDirectory()));
        var profile = await profileProvider.LoadAsync(cancellationToken);

        var ollamaOptions = new OllamaOptions { Model = commandLine.Model };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<OllamaOptions>>(Options.Create(ollamaOptions));
        services.AddHttpClient<IJobMatchingService, OllamaJobMatchingService>(client =>
        {
            client.BaseAddress = new Uri(ollamaOptions.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(ollamaOptions.TimeoutSeconds);
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var runner = new BenchmarkRunner(serviceProvider.GetRequiredService<IJobMatchingService>());
        var result = await runner.RunAsync(
            dataset,
            profile,
            commandLine.Model,
            commandLine.CaseFilter,
            cancellationToken);
        var resultPath = await BenchmarkResultStore.SaveAsync(
            result,
            outputDirectory,
            cancellationToken);

        Console.WriteLine($"Benchmark run {result.RunId} saved to {resultPath}");
        Console.WriteLine(JsonSerializer.Serialize(result.AggregateMetrics, BenchmarkJson.SerializerOptions));
        if (result.AggregateMetrics.ExactMatchAccuracy is null)
        {
            Console.WriteLine("Ground-truth accuracy and MAE were not calculated because no approved labels were successfully scored.");
        }

        return 0;
    }

    private static async Task<int> CompareAsync(
        IReadOnlyList<string> comparisonPaths,
        CancellationToken cancellationToken)
    {
        var results = new List<BenchmarkRunResult>(comparisonPaths.Count);
        foreach (var path in comparisonPaths)
        {
            results.Add(await BenchmarkResultStore.LoadAsync(Path.GetFullPath(path), cancellationToken));
        }

        var report = BenchmarkComparison.Compare(results);
        Console.WriteLine(JsonSerializer.Serialize(report, BenchmarkJson.SerializerOptions));
        if (report.PromptVersionsDiffer)
        {
            Console.WriteLine("Prompt versions differ; interpret model deltas as different experimental configurations.");
        }

        Console.WriteLine("No model is declared a winner; review coverage, quality, failures, latency, and disagreements together.");
        return 0;
    }

    private static async Task<int> ExtractAsync(
        BenchmarkCommandLineOptions commandLine,
        CancellationToken cancellationToken)
    {
        var dataset = await BenchmarkDatasetLoader.LoadAsync(
            Path.GetFullPath(commandLine.DatasetPath),
            cancellationToken);
        var benchmarkCase = dataset.Dataset.Cases.SingleOrDefault(item => string.Equals(
            item.Id,
            commandLine.CaseFilter,
            StringComparison.OrdinalIgnoreCase));
        if (benchmarkCase is null)
        {
            throw new BenchmarkValidationException(
                $"Case filter '{commandLine.CaseFilter}' did not match a benchmark case ID.");
        }

        var ollamaOptions = new OllamaOptions { Model = commandLine.Model };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<OllamaOptions>>(Options.Create(ollamaOptions));
        services.AddHttpClient<IJobRequirementsExtractor, OllamaJobRequirementsExtractor>(client =>
        {
            client.BaseAddress = new Uri(ollamaOptions.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(ollamaOptions.TimeoutSeconds);
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var extractor = serviceProvider.GetRequiredService<IJobRequirementsExtractor>();
        var extraction = await extractor.ExtractAsync(new AIJobSearchAgent.Domain.Entities.JobVacancy
        {
            ExternalId = benchmarkCase.Id,
            Source = "SyntheticBenchmark",
            Title = benchmarkCase.VacancyTitle,
            Company = benchmarkCase.Company,
            Location = benchmarkCase.Location,
            IsRemote = benchmarkCase.IsRemote,
            Description = benchmarkCase.Description,
            Url = benchmarkCase.VacancyUrl ?? string.Empty
        }, cancellationToken);

        Console.WriteLine(JsonSerializer.Serialize(extraction, BenchmarkJson.SerializerOptions));
        return 0;
    }
}
