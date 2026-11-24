using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationLifecycleAndTenantPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "confirmation_outcome",
                table: "citas",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_state",
                table: "citas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deadline_missed_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "final_confirmation_requested_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "orchestration_suppressed",
                table: "citas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "citas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.CreateTable(
                name: "tenant_confirmation_policies",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reminder1_offset_hours = table.Column<int>(type: "integer", nullable: false),
                    reminder2_offset_hours = table.Column<int>(type: "integer", nullable: false),
                    final_confirmation_offset_hours = table.Column<int>(type: "integer", nullable: false),
                    deadline_offset_hours = table.Column<int>(type: "integer", nullable: false),
                    minimum_delay_after_creation_minutes = table.Column<int>(type: "integer", nullable: false),
                    same_day_reminder_min_lead_hours = table.Column<int>(type: "integer", nullable: false),
                    timezone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quiet_hours_start_local = table.Column<TimeOnly>(type: "time", nullable: true),
                    quiet_hours_end_local = table.Column<TimeOnly>(type: "time", nullable: true),
                    primary_channel_strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    alternate_channel_strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enable_rescore_at_reminder2 = table.Column<bool>(type: "boolean", nullable: false),
                    count_sent_as_delivered_for_unanswered = table.Column<bool>(type: "boolean", nullable: false),
                    weight_confirmed_early = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    weight_reminder_unanswered = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    max_reminder_unanswered_cumulative = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    weight_delivery_failed = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    weight_replied_without_confirmation = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    weight_final_confirmed = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    medium_exit_threshold = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    medium_enter_threshold = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    high_exit_threshold = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    high_enter_threshold = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_confirmation_policies", x => x.tenant_id);
                    table.CheckConstraint("CK_tenant_confirmation_policies_hysteresis", "medium_exit_threshold > 0 AND medium_exit_threshold < medium_enter_threshold AND medium_enter_threshold < high_exit_threshold AND high_exit_threshold < high_enter_threshold AND high_enter_threshold < 1");
                    table.CheckConstraint("CK_tenant_confirmation_policies_offsets", "reminder1_offset_hours > reminder2_offset_hours AND reminder2_offset_hours > final_confirmation_offset_hours AND final_confirmation_offset_hours > deadline_offset_hours AND deadline_offset_hours > 0 AND minimum_delay_after_creation_minutes >= 0 AND same_day_reminder_min_lead_hours >= 0 AND same_day_reminder_min_lead_hours <= deadline_offset_hours");
                    table.CheckConstraint("CK_tenant_confirmation_policies_quiet_hours", "(quiet_hours_start_local IS NULL AND quiet_hours_end_local IS NULL) OR (quiet_hours_start_local IS NOT NULL AND quiet_hours_end_local IS NOT NULL AND quiet_hours_start_local <> quiet_hours_end_local)");
                    table.CheckConstraint("CK_tenant_confirmation_policies_weights", "weight_confirmed_early <= 0 AND weight_reminder_unanswered >= 0 AND max_reminder_unanswered_cumulative >= 0 AND weight_delivery_failed >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_citas_calibration_outcome",
                table: "citas",
                columns: new[] { "tenant_id", "confirmation_outcome", "scored_at" },
                filter: "confirmation_outcome IS NOT NULL");

            migrationBuilder.Sql(
                """
                INSERT INTO tenant_confirmation_policies (
                    tenant_id,
                    reminder1_offset_hours,
                    reminder2_offset_hours,
                    final_confirmation_offset_hours,
                    deadline_offset_hours,
                    minimum_delay_after_creation_minutes,
                    same_day_reminder_min_lead_hours,
                    timezone_id,
                    quiet_hours_start_local,
                    quiet_hours_end_local,
                    primary_channel_strategy,
                    alternate_channel_strategy,
                    enable_rescore_at_reminder2,
                    count_sent_as_delivered_for_unanswered,
                    weight_confirmed_early,
                    weight_reminder_unanswered,
                    max_reminder_unanswered_cumulative,
                    weight_delivery_failed,
                    weight_replied_without_confirmation,
                    weight_final_confirmed,
                    medium_exit_threshold,
                    medium_enter_threshold,
                    high_exit_threshold,
                    high_enter_threshold,
                    created_at,
                    updated_at
                )
                VALUES (
                    '11111111-1111-1111-1111-111111111111',
                    72,
                    48,
                    24,
                    12,
                    30,
                    3,
                    'America/Santiago',
                    NULL,
                    NULL,
                    'PatientPreferred',
                    'AlternateIfAvailable',
                    TRUE,
                    FALSE,
                    -1.5000,
                    0.4000,
                    1.2000,
                    0.8000,
                    0.0000,
                    0.0000,
                    0.4000,
                    0.5000,
                    0.5500,
                    0.7000,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                )
                ON CONFLICT (tenant_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM tenant_confirmation_policies WHERE tenant_id = '11111111-1111-1111-1111-111111111111';");

            migrationBuilder.DropTable(
                name: "tenant_confirmation_policies");

            migrationBuilder.DropIndex(
                name: "IX_citas_calibration_outcome",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "confirmation_outcome",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "confirmation_state",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "deadline_missed_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "final_confirmation_requested_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "orchestration_suppressed",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "citas");
        }
    }
}
