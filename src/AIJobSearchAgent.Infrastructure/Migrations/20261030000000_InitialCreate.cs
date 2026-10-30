using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJobSearchAgent.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JobVacancies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ExternalId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Company = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Location = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                IsRemote = table.Column<bool>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                PublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_JobVacancies", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_JobVacancies_Source_ExternalId",
            table: "JobVacancies",
            columns: new[] { "Source", "ExternalId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "JobVacancies");
    }
}
