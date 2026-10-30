using AIJobSearchAgent.Application.Abstractions;
using AIJobSearchAgent.Infrastructure.Persistence;
using AIJobSearchAgent.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIJobSearchAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string databasePath)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IJobVacancyRepository, JobVacancyRepository>();

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

        return services;
    }
}
