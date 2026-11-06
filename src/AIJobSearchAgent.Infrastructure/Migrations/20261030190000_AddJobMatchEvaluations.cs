using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJobSearchAgent.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030190000_AddJobMatchEvaluations")]
public partial class AddJobMatchEvaluations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JobMatchEvaluations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                JobVacancyId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProfileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ModelName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PromptVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Rating = table.Column<int>(type: "INTEGER", nullable: true),
                MatchedSkills = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                MissingRequiredSkills = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                MissingOptionalSkills = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                Explanation = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                EvaluationStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                EvaluatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobMatchEvaluations", x => x.Id);
                table.CheckConstraint(
                    "CK_JobMatchEvaluations_Rating",
                    "Rating IS NULL OR (Rating >= 1 AND Rating <= 5)");
                table.ForeignKey(
                    name: "FK_JobMatchEvaluations_JobVacancies_JobVacancyId",
                    column: x => x.JobVacancyId,
                    principalTable: "JobVacancies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JobMatchEvaluations_JobVacancyId_EvaluatedAt",
            table: "JobMatchEvaluations",
            columns: new[] { "JobVacancyId", "EvaluatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "JobMatchEvaluations");
    }
}
