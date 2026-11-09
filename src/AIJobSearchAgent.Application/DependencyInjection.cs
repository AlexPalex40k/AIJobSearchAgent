using AIJobSearchAgent.Application.Services;
using AIJobSearchAgent.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace AIJobSearchAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddSingleton<JobMatchCalculator>()
        .AddScoped<ImportJobsUseCase>()
        .AddScoped<GetJobsUseCase>()
        .AddScoped<EvaluateJobUseCase>()
        .AddScoped<GetJobEvaluationsUseCase>();
}
