using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicRiskSignalsAndEffectiveScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "effective_band",
                table: "citas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "latest_model_score",
                table: "citas",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rescored_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "score_efectivo",
                table: "citas",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "score_modelo",
                table: "citas",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "risk_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_signals", x => x.id);
                    table.ForeignKey(
                        name: "FK_risk_signals_citas_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_risk_signals_appointment_occurred",
                table: "risk_signals",
                columns: new[] { "appointment_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "UX_risk_signals_appointment_idempotency",
                table: "risk_signals",
                columns: new[] { "appointment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.Sql(
                """
                UPDATE citas
                SET score_modelo = score_riesgo,
                    score_efectivo = score_riesgo,
                    effective_band = CASE
                        WHEN LOWER(COALESCE(banda_riesgo, '')) IN ('alto', 'critico', 'crítico', 'high') THEN 'High'
                        WHEN LOWER(COALESCE(banda_riesgo, '')) IN ('medio', 'moderado', 'medium') THEN 'Medium'
                        WHEN LOWER(COALESCE(banda_riesgo, '')) IN ('bajo', 'low') THEN 'Low'
                        WHEN score_riesgo >= 0.70 THEN 'High'
                        WHEN score_riesgo >= 0.50 THEN 'Medium'
                        ELSE 'Low'
                    END
                WHERE score_riesgo IS NOT NULL
                  AND score_modelo IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_signals");

            migrationBuilder.DropColumn(
                name: "effective_band",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "latest_model_score",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "rescored_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "score_efectivo",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "score_modelo",
                table: "citas");
        }
    }
}
