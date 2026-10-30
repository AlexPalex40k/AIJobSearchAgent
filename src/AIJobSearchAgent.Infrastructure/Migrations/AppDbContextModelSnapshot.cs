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
#pragma warning restore 612, 618
    }
}
