using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobVacancy> JobVacancies => Set<JobVacancy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
