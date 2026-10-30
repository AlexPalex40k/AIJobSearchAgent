using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class JobVacancyConfiguration : IEntityTypeConfiguration<JobVacancy>
{
    public void Configure(EntityTypeBuilder<JobVacancy> builder)
    {
        builder.ToTable("JobVacancies");
        builder.HasKey(vacancy => vacancy.Id);

        builder.Property(vacancy => vacancy.ExternalId).IsRequired().HasMaxLength(300);
        builder.Property(vacancy => vacancy.Source).IsRequired().HasMaxLength(100);
        builder.Property(vacancy => vacancy.Title).IsRequired().HasMaxLength(500);
        builder.Property(vacancy => vacancy.Company).IsRequired().HasMaxLength(300);
        builder.Property(vacancy => vacancy.Location).IsRequired().HasMaxLength(300);
        builder.Property(vacancy => vacancy.Description).IsRequired().HasMaxLength(100_000);
        builder.Property(vacancy => vacancy.Url).IsRequired().HasMaxLength(2_000);

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

        builder.Property(vacancy => vacancy.PublishedAt).HasConversion(nullableDateTimeOffsetConverter);
        builder.Property(vacancy => vacancy.CreatedAt).HasConversion(dateTimeOffsetConverter);

        builder.HasIndex(vacancy => new { vacancy.Source, vacancy.ExternalId }).IsUnique();
    }
}
