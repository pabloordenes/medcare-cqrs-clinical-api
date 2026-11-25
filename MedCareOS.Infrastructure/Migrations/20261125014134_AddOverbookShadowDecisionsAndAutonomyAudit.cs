using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverbookShadowDecisionsAndAutonomyAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "overbook_policy_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_auto_overbook_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    new_auto_overbook_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evaluated_count_at_change = table.Column<int>(type: "integer", nullable: false),
                    false_positive_rate_at_change = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    gate_passed = table.Column<bool>(type: "boolean", nullable: false),
                    forced_override = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overbook_policy_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "overbook_shadow_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_block_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_risk_score = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    threshold_used = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    decision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    would_have_overbooked = table.Column<bool>(type: "boolean", nullable: false),
                    final_notification_delivered = table.Column<bool>(type: "boolean", nullable: false),
                    has_clinical_exclusion = table.Column<bool>(type: "boolean", nullable: false),
                    clinical_exclusion_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    outcome_attendance = table.Column<bool>(type: "boolean", nullable: true),
                    is_synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome_resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overbook_shadow_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_overbook_shadow_decisions_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_overbook_policy_audit_logs_tenant_time",
                table: "overbook_policy_audit_logs",
                columns: new[] { "tenant_id", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_overbook_shadow_decisions_cita_id",
                table: "overbook_shadow_decisions",
                column: "cita_id");

            migrationBuilder.CreateIndex(
                name: "IX_overbook_shadow_decisions_tenant_eval",
                table: "overbook_shadow_decisions",
                columns: new[] { "tenant_id", "would_have_overbooked", "is_synthetic" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overbook_policy_audit_logs");

            migrationBuilder.DropTable(
                name: "overbook_shadow_decisions");
        }
    }
}
