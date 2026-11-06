using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobVacancy> JobVacancies => Set<JobVacancy>();
    public DbSet<JobMatchEvaluation> JobMatchEvaluations => Set<JobMatchEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
