using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJobSearchAgent.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
public partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "9.0.10");

        modelBuilder.Entity("AIJobSearchAgent.Domain.Entities.JobMatchEvaluation", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("EvaluatedAt")
                .HasConversion<long>()
                .HasColumnType("INTEGER");

            entity.Property<int>("EvaluationStatus")
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasColumnType("TEXT");

            entity.Property<string>("Explanation")
                .IsRequired()
                .HasMaxLength(10000)
                .HasColumnType("TEXT");

            entity.Property<Guid>("JobVacancyId")
                .HasColumnType("TEXT");

            entity.Property<List<string>>("MatchedSkills")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(10000)
                .HasColumnType("TEXT");

            entity.Property<List<string>>("MissingOptionalSkills")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(10000)
                .HasColumnType("TEXT");

            entity.Property<List<string>>("MissingRequiredSkills")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(10000)
                .HasColumnType("TEXT");

            entity.Property<string>("ModelName")
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnType("TEXT");

            entity.Property<string>("ProfileHash")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT");

            entity.Property<string>("PromptVersion")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("TEXT");

            entity.Property<int?>("Rating")
                .HasColumnType("INTEGER");

            entity.HasKey("Id");

            entity.HasIndex("JobVacancyId", "EvaluatedAt");

            entity.ToTable("JobMatchEvaluations", table =>
                table.HasCheckConstraint(
                    "CK_JobMatchEvaluations_Rating",
                    "Rating IS NULL OR (Rating >= 1 AND Rating <= 5)"));
        });

        modelBuilder.Entity("AIJobSearchAgent.Domain.Entities.JobVacancy", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("TEXT");

            entity.Property<string>("Company")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset>("CreatedAt")
                .HasConversion<long>()
                .HasColumnType("INTEGER");

            entity.Property<string>("Description")
                .IsRequired()
                .HasMaxLength(100000)
                .HasColumnType("TEXT");

            entity.Property<string>("ExternalId")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("TEXT");

            entity.Property<bool>("IsRemote")
                .HasColumnType("INTEGER");

            entity.Property<string>("Location")
                .IsRequired()
                .HasMaxLength(300)
                .HasColumnType("TEXT");

            entity.Property<DateTimeOffset?>("PublishedAt")
                .HasConversion<long?>()
                .HasColumnType("INTEGER");

            entity.Property<string>("Source")
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnType("TEXT");

            entity.Property<string>("Title")
                .IsRequired()
                .HasMaxLength(500)
                .HasColumnType("TEXT");

            entity.Property<string>("Url")
                .IsRequired()
                .HasMaxLength(2000)
                .HasColumnType("TEXT");

            entity.HasKey("Id");

            entity.HasIndex("Source", "ExternalId")
                .IsUnique();

            entity.ToTable("JobVacancies");
        });

        modelBuilder.Entity("AIJobSearchAgent.Domain.Entities.JobMatchEvaluation", entity =>
        {
            entity.HasOne("AIJobSearchAgent.Domain.Entities.JobVacancy", "JobVacancy")
                .WithMany()
                .HasForeignKey("JobVacancyId")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();

            entity.Navigation("JobVacancy");
        });
#pragma warning restore 612, 618
    }
}
