using AIJobSearchAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIJobSearchAgent.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261106000000_EnforceEvaluationStatusRatingConsistency")]
public partial class EnforceEvaluationStatusRatingConsistency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        RebuildTable(
            migrationBuilder,
            "((\"EvaluationStatus\" = 'Succeeded' AND \"Rating\" IS NOT NULL AND \"Rating\" >= 1 AND \"Rating\" <= 5) " +
            "OR (\"EvaluationStatus\" IN ('Failed', 'TimedOut') AND \"Rating\" IS NULL))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RebuildTable(
            migrationBuilder,
            "\"Rating\" IS NULL OR (\"Rating\" >= 1 AND \"Rating\" <= 5)");
    }

    private static void RebuildTable(MigrationBuilder migrationBuilder, string checkConstraint)
    {
        migrationBuilder.Sql($$"""
            CREATE TABLE "JobMatchEvaluations_new" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_JobMatchEvaluations" PRIMARY KEY,
                "JobVacancyId" TEXT NOT NULL,
                "ProfileHash" TEXT NOT NULL,
                "ModelName" TEXT NOT NULL,
                "PromptVersion" TEXT NOT NULL,
                "Rating" INTEGER NULL,
                "MatchedSkills" TEXT NOT NULL,
                "MissingRequiredSkills" TEXT NOT NULL,
                "MissingOptionalSkills" TEXT NOT NULL,
                "Explanation" TEXT NOT NULL,
                "EvaluationStatus" TEXT NOT NULL,
                "EvaluatedAt" INTEGER NOT NULL,
                CONSTRAINT "CK_JobMatchEvaluations_Rating" CHECK ({{checkConstraint}}),
                CONSTRAINT "FK_JobMatchEvaluations_JobVacancies_JobVacancyId"
                    FOREIGN KEY ("JobVacancyId") REFERENCES "JobVacancies" ("Id") ON DELETE RESTRICT
            );
            """);

        migrationBuilder.Sql("""
            INSERT INTO "JobMatchEvaluations_new" (
                "Id", "JobVacancyId", "ProfileHash", "ModelName", "PromptVersion", "Rating",
                "MatchedSkills", "MissingRequiredSkills", "MissingOptionalSkills", "Explanation",
                "EvaluationStatus", "EvaluatedAt")
            SELECT
                "Id", "JobVacancyId", "ProfileHash", "ModelName", "PromptVersion", "Rating",
                "MatchedSkills", "MissingRequiredSkills", "MissingOptionalSkills", "Explanation",
                "EvaluationStatus", "EvaluatedAt"
            FROM "JobMatchEvaluations";
            """);

        migrationBuilder.Sql("DROP TABLE \"JobMatchEvaluations\";");
        migrationBuilder.Sql("ALTER TABLE \"JobMatchEvaluations_new\" RENAME TO \"JobMatchEvaluations\";");
        migrationBuilder.Sql("""
            CREATE INDEX "IX_JobMatchEvaluations_JobVacancyId_EvaluatedAt"
            ON "JobMatchEvaluations" ("JobVacancyId", "EvaluatedAt");
            """);
    }
}
