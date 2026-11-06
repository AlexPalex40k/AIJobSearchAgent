using System.Text.Json;
using AIJobSearchAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIJobSearchAgent.Infrastructure.Persistence;

public sealed class JobMatchEvaluationConfiguration : IEntityTypeConfiguration<JobMatchEvaluation>
{
    public void Configure(EntityTypeBuilder<JobMatchEvaluation> builder)
    {
        builder.ToTable("JobMatchEvaluations", table => table.HasCheckConstraint(
            "CK_JobMatchEvaluations_Rating",
            "((EvaluationStatus = 'Succeeded' AND Rating IS NOT NULL AND Rating >= 1 AND Rating <= 5) OR " +
            "(EvaluationStatus IN ('Failed', 'TimedOut') AND Rating IS NULL))"));
        builder.HasKey(evaluation => evaluation.Id);

        builder.Property(evaluation => evaluation.ProfileHash).IsRequired().HasMaxLength(64);
        builder.Property(evaluation => evaluation.ModelName).IsRequired().HasMaxLength(200);
        builder.Property(evaluation => evaluation.PromptVersion).IsRequired().HasMaxLength(100);
        builder.Property(evaluation => evaluation.Explanation).IsRequired().HasMaxLength(10_000);
        builder.Property(evaluation => evaluation.EvaluationStatus)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        var listConverter = new ValueConverter<List<string>, string>(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => JsonSerializer.Deserialize<List<string>>(value, (JsonSerializerOptions?)null) ?? new List<string>());
        var listComparer = new ValueComparer<List<string>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToList());

        ConfigureList(builder.Property(evaluation => evaluation.MatchedSkills), listConverter, listComparer);
        ConfigureList(builder.Property(evaluation => evaluation.MissingRequiredSkills), listConverter, listComparer);
        ConfigureList(builder.Property(evaluation => evaluation.MissingOptionalSkills), listConverter, listComparer);

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        builder.Property(evaluation => evaluation.EvaluatedAt).HasConversion(dateTimeOffsetConverter);

        builder.HasOne(evaluation => evaluation.JobVacancy)
            .WithMany()
            .HasForeignKey(evaluation => evaluation.JobVacancyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(evaluation => new { evaluation.JobVacancyId, evaluation.EvaluatedAt });
    }

    private static void ConfigureList(
        PropertyBuilder<List<string>> property,
        ValueConverter<List<string>, string> converter,
        ValueComparer<List<string>> comparer)
    {
        property.HasConversion(converter).HasMaxLength(10_000);
        property.Metadata.SetValueComparer(comparer);
    }
}
