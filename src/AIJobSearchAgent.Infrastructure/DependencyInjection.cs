using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Infrastructure.Persistence;
using AIJobSearchAgent.Infrastructure.Matching;
using AIJobSearchAgent.Infrastructure.Profiles;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AIJobSearchAgent.LinkedInSpike;

namespace AIJobSearchAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string databasePath,
        string solutionRoot)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IJobVacancyRepository, JobVacancyRepository>();
        services.AddScoped<IRankedJobVacancyQuery, RankedJobVacancyQuery>();
        services.AddScoped<IJobMatchEvaluationRepository, JobMatchEvaluationRepository>();

        services.AddOptions<ArbeitnowOptions>()
            .Bind(configuration.GetSection(ArbeitnowOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IJobProvider, ArbeitnowJobProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ArbeitnowOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AIJobSearchAgent/1.0");
        });

        services.AddOptions<LinkedInOptions>()
            .Bind(configuration.GetSection(LinkedInOptions.SectionName));
        services.AddTransient<LinkedInHtmlParser>();
        services.AddTransient<AccessRestrictionDetector>();
        services.AddTransient<SeleniumExperiment>();
        services.AddTransient<ILinkedInSearchScraper, LinkedInSeleniumSearchScraper>();
        services.AddScoped<LinkedInJobProvider>(serviceProvider => new LinkedInJobProvider(
            serviceProvider.GetRequiredService<ILinkedInSearchScraper>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LinkedInJobProvider>>()));
        services.AddScoped<ILinkedInJobProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<LinkedInJobProvider>());

        services.AddOptions<CandidateProfileOptions>()
            .Bind(configuration.GetSection(CandidateProfileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(new CandidateProfileFileLocation(solutionRoot));
        services.AddScoped<ICandidateProfileProvider, CandidateProfileFileProvider>();

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IJobMatchingService, OllamaJobMatchingService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
