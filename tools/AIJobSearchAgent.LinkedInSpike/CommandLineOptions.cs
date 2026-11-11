namespace AIJobSearchAgent.LinkedInSpike;

public enum DiagnosticMode
{
    Http,
    Selenium
}

public sealed record CommandLineOptions(
    DiagnosticMode Mode,
    Uri Url,
    int MaxJobs,
    string? BrowserBinaryPath,
    bool WriteJson,
    bool Headless)
{
    public static CommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            return CommandLineParseResult.Help();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var writeJson = false;
        var headless = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase))
            {
                writeJson = true;
                continue;
            }

            if (string.Equals(argument, "--headless", StringComparison.OrdinalIgnoreCase))
            {
                headless = true;
                continue;
            }

            if (argument is not ("--mode" or "--url" or "--max-jobs" or "--browser-binary"))
            {
                return CommandLineParseResult.Failure($"Unknown argument '{argument}'.");
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return CommandLineParseResult.Failure($"Argument '{argument}' requires a value.");
            }

            if (!values.TryAdd(argument, args[++index]))
            {
                return CommandLineParseResult.Failure($"Argument '{argument}' was supplied more than once.");
            }
        }

        if (!values.TryGetValue("--mode", out var modeValue) ||
            !Enum.TryParse<DiagnosticMode>(modeValue, ignoreCase: true, out var mode))
        {
            return CommandLineParseResult.Failure("--mode must be either 'http' or 'selenium'.");
        }

        if (!values.TryGetValue("--url", out var urlValue) ||
            !Uri.TryCreate(urlValue, UriKind.Absolute, out var url) ||
            !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsLinkedInHost(url.Host) ||
            !IsJobsSearchPath(url.AbsolutePath))
        {
            return CommandLineParseResult.Failure(
                "--url must be an absolute HTTPS URL under linkedin.com/jobs/search.");
        }

        var maxJobs = 10;
        if (values.TryGetValue("--max-jobs", out var maxJobsValue) &&
            (!int.TryParse(maxJobsValue, out maxJobs) || maxJobs is < 1 or > 10))
        {
            return CommandLineParseResult.Failure("--max-jobs must be an integer from 1 through 10.");
        }

        values.TryGetValue("--browser-binary", out var browserBinaryPath);
        if (mode == DiagnosticMode.Http && browserBinaryPath is not null)
        {
            return CommandLineParseResult.Failure("--browser-binary can only be used in selenium mode.");
        }

        if (mode == DiagnosticMode.Http && headless)
        {
            return CommandLineParseResult.Failure("--headless can only be used in selenium mode.");
        }

        if (browserBinaryPath is not null && !File.Exists(browserBinaryPath))
        {
            return CommandLineParseResult.Failure("The supplied browser binary path does not exist.");
        }

        return CommandLineParseResult.Success(
            new CommandLineOptions(mode, url, maxJobs, browserBinaryPath, writeJson, headless));
    }

    private static bool IsLinkedInHost(string host) =>
        string.Equals(host, "linkedin.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsJobsSearchPath(string path) =>
        string.Equals(path, "/jobs/search", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/jobs/search/", StringComparison.OrdinalIgnoreCase);
}

public sealed record CommandLineParseResult(
    CommandLineOptions? Options,
    string? Error,
    bool ShowHelp)
{
    public bool IsSuccess => Options is not null;

    public static CommandLineParseResult Success(CommandLineOptions options) =>
        new(options, null, false);

    public static CommandLineParseResult Failure(string error) =>
        new(null, error, false);

    public static CommandLineParseResult Help() =>
        new(null, null, true);
}
