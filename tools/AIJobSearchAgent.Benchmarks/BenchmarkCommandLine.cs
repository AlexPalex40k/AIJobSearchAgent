namespace AIJobSearchAgent.Benchmarks;

public sealed record BenchmarkCommandLineOptions(
    bool ShowHelp,
    bool IsComparison,
    bool IsExtraction,
    string DatasetPath,
    string ProfilePath,
    string Model,
    string OutputDirectory,
    string? CaseFilter,
    IReadOnlyList<string> ComparisonPaths);

public static class BenchmarkCommandLine
{
    public static BenchmarkCommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return Defaults();
        }

        if (arguments[0] is "--help" or "-h")
        {
            return Defaults() with { ShowHelp = true };
        }

        if (string.Equals(arguments[0], "compare", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Count < 3)
            {
                throw new BenchmarkValidationException("Comparison requires at least two result file paths.");
            }

            return Defaults() with
            {
                IsComparison = true,
                ComparisonPaths = arguments.Skip(1).ToArray()
            };
        }

        var firstOptionIndex = 0;
        var options = Defaults();
        if (string.Equals(arguments[0], "extract", StringComparison.OrdinalIgnoreCase))
        {
            options = options with { IsExtraction = true };
            firstOptionIndex = 1;
        }

        for (var index = firstOptionIndex; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                throw new BenchmarkValidationException($"Missing value for command-line option '{arguments[index]}'.");
            }

            var value = arguments[index + 1];
            options = arguments[index] switch
            {
                "--dataset" => options with { DatasetPath = value },
                "--profile" => options with { ProfilePath = value },
                "--model" => options with { Model = value },
                "--output" => options with { OutputDirectory = value },
                "--case" => options with { CaseFilter = value },
                _ => throw new BenchmarkValidationException(
                    $"Unknown command-line option '{arguments[index]}'. Use --help for supported options.")
            };
        }

        if (string.IsNullOrWhiteSpace(options.Model) || options.Model.Length > 200)
        {
            throw new BenchmarkValidationException("Model must contain between 1 and 200 characters.");
        }

        if (options.IsExtraction && string.IsNullOrWhiteSpace(options.CaseFilter))
        {
            throw new BenchmarkValidationException("Extraction inspection requires --case with an exact benchmark case ID.");
        }

        return options;
    }

    public static string HelpText => """
        Run a benchmark:
          dotnet run --project tools/AIJobSearchAgent.Benchmarks -- [options]

        Options:
          --dataset <path>  Dataset JSON path (default: benchmarks/dataset.v1.json)
          --profile <path>  Synthetic candidate profile path (default: benchmarks/candidate-profile.synthetic.json)
          --model <name>    Ollama model (default: qwen3:1.7b)
          --output <path>   Result directory (default: benchmarks/results)
          --case <id>       Run one exact benchmark case ID
          --help            Show this help

        Compare saved runs without Ollama:
          dotnet run --project tools/AIJobSearchAgent.Benchmarks -- compare <result1.json> <result2.json> [more.json]

        Inspect structured requirements for one benchmark vacancy without calculating metrics:
          dotnet run --project tools/AIJobSearchAgent.Benchmarks -- extract --case <id> [--model <name>] [--dataset <path>]
        """;

    private static BenchmarkCommandLineOptions Defaults() => new(
        ShowHelp: false,
        IsComparison: false,
        IsExtraction: false,
        DatasetPath: Path.Combine("benchmarks", "dataset.v1.json"),
        ProfilePath: Path.Combine("benchmarks", "candidate-profile.synthetic.json"),
        Model: "qwen3:1.7b",
        OutputDirectory: Path.Combine("benchmarks", "results"),
        CaseFilter: null,
        ComparisonPaths: []);
}
