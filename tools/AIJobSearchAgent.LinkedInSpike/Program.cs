using System.Net;

namespace AIJobSearchAgent.LinkedInSpike;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parsed = CommandLineOptions.Parse(args);
        if (parsed.ShowHelp)
        {
            WriteUsage();
            return 0;
        }

        if (!parsed.IsSuccess)
        {
            Console.Error.WriteLine(parsed.Error);
            WriteUsage();
            return 2;
        }

        var options = parsed.Options!;
        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        try
        {
            var parser = new LinkedInHtmlParser();
            var detector = new AccessRestrictionDetector();
            DiagnosticReport report;

            if (options.Mode == DiagnosticMode.Http)
            {
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    AutomaticDecompression = DecompressionMethods.All
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };
                report = await new HttpExperiment(client, parser, detector).RunAsync(
                    options.Url,
                    options.MaxJobs,
                    cancellationSource.Token);
            }
            else
            {
                report = await new SeleniumExperiment(parser, detector).RunAsync(
                    options.Url,
                    options.MaxJobs,
                    options.BrowserBinaryPath,
                    options.Headless,
                    cancellationSource.Token);
            }

            ConsoleReportWriter.Write(report);
            if (options.WriteJson)
            {
                var outputPath = await ConsoleReportWriter.WriteJsonAsync(report, cancellationSource.Token);
                Console.WriteLine();
                Console.WriteLine($"JSON report: {outputPath}");
            }

            return report.Errors.Count == 0 ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Diagnostic cancelled.");
            return 130;
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            "Usage: dotnet run --project tools/AIJobSearchAgent.LinkedInSpike -- " +
            "--mode http|selenium --url \"<public LinkedIn jobs URL>\" " +
            "[--max-jobs 1..10] [--browser-binary <path>] [--headless] [--json]");
    }
}
