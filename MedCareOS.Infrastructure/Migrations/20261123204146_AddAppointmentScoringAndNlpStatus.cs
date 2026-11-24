using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentScoringAndNlpStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "score_riesgo",
                table: "citas",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)");

            migrationBuilder.AddColumn<string>(
                name: "last_scoring_error",
                table: "citas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_scoring_retry_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nlp_status",
                table: "citas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scored_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "scoring_attempts",
                table: "citas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "scoring_status",
                table: "citas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.CreateIndex(
                name: "IX_citas_scoring_retry",
                table: "citas",
                columns: new[] { "scoring_status", "nlp_status", "next_scoring_retry_at" },
                filter: "estado IN ('Scheduled', 'Confirmed') AND (scoring_status <> 'Scored' OR nlp_status IN ('Pending', 'Failed'))");

            migrationBuilder.Sql("""
                UPDATE citas
                SET scoring_status = 'Pending',
                    score_riesgo = NULL,
                    banda_riesgo = NULL
                WHERE banda_riesgo IS NULL OR LOWER(TRIM(banda_riesgo)) = 'pendiente';

                UPDATE citas
                SET scoring_status = 'Scored',
                    scored_at = COALESCE("UpdatedAt", NOW())
                WHERE banda_riesgo IS NOT NULL AND LOWER(TRIM(banda_riesgo)) <> 'pendiente';

                UPDATE citas
                SET nlp_status = 'Skipped',
                    resumen_nlp = NULL
                WHERE COALESCE(TRIM(sintomas_texto), '') = '';

                UPDATE citas
                SET nlp_status = 'Failed',
                    resumen_nlp = NULL
                WHERE COALESCE(TRIM(sintomas_texto), '') <> ''
                  AND resumen_nlp IS NOT NULL
                  AND TRIM(resumen_nlp) = TRIM(sintomas_texto);

                UPDATE citas
                SET nlp_status = 'Completed'
                WHERE COALESCE(TRIM(sintomas_texto), '') <> ''
                  AND resumen_nlp IS NOT NULL
                  AND TRIM(resumen_nlp) <> TRIM(sintomas_texto);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE citas SET score_riesgo = 0 WHERE score_riesgo IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_citas_scoring_retry",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "last_scoring_error",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "next_scoring_retry_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "nlp_status",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "scored_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "scoring_attempts",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "scoring_status",
                table: "citas");

            migrationBuilder.AlterColumn<decimal>(
                name: "score_riesgo",
                table: "citas",
                type: "numeric(5,4)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);
        }
    }
}
