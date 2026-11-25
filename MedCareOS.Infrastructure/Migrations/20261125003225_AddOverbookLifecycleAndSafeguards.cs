using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverbookLifecycleAndSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_overbook_enabled",
                table: "tenant_confirmation_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "auto_overbook_min_effective_score",
                table: "tenant_confirmation_policies",
                type: "numeric(5,4)",
                nullable: false,
                defaultValue: 0.8500m);

            migrationBuilder.AddColumn<string>(
                name: "excluded_clinical_keywords_json",
                table: "tenant_confirmation_policies",
                type: "jsonb",
                nullable: false,
                defaultValue: "[\"pre-operatorio\",\"preoperatorio\",\"cirugia\",\"embarazo alto riesgo\"]");

            migrationBuilder.AddColumn<string>(
                name: "excluded_clinical_specialties_json",
                table: "tenant_confirmation_policies",
                type: "jsonb",
                nullable: false,
                defaultValue: "[\"oncologia\",\"preoperatorio\",\"cirugia\"]");

            migrationBuilder.AddColumn<int>(
                name: "excluded_senior_age_years",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 70);

            migrationBuilder.AddColumn<decimal>(
                name: "hitl_overbook_min_effective_score",
                table: "tenant_confirmation_policies",
                type: "numeric(5,4)",
                nullable: false,
                defaultValue: 0.5000m);

            migrationBuilder.AddColumn<decimal>(
                name: "max_false_positive_rate_for_activation",
                table: "tenant_confirmation_policies",
                type: "numeric(5,4)",
                nullable: false,
                defaultValue: 0.1500m);

            migrationBuilder.AddColumn<int>(
                name: "max_reschedules_per_appointment",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "max_waiting_list_cascade_offers",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "min_lead_minutes_for_waiting_list_offer",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "min_shadow_evaluations_for_activation",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "waiting_list_offer_ttl_minutes",
                table: "tenant_confirmation_policies",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "schedule_blocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                table: "schedule_blocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deactivation_reason",
                table: "schedule_blocks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "occupied_appointment_id",
                table: "schedule_blocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "overbook_status",
                table: "schedule_blocks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "schedule_blocks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "response_body_json",
                table: "idempotency_keys",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "response_status_code",
                table: "idempotency_keys",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenant_confirmation_policies_overbook",
                table: "tenant_confirmation_policies",
                sql: "hitl_overbook_min_effective_score > 0 AND hitl_overbook_min_effective_score < auto_overbook_min_effective_score AND auto_overbook_min_effective_score < 1 AND excluded_senior_age_years BETWEEN 18 AND 120 AND waiting_list_offer_ttl_minutes > 0 AND max_waiting_list_cascade_offers >= 1 AND min_lead_minutes_for_waiting_list_offer >= 0 AND max_reschedules_per_appointment >= 0 AND min_shadow_evaluations_for_activation >= 1 AND max_false_positive_rate_for_activation > 0 AND max_false_positive_rate_for_activation < 1");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_blocks_active_overbooks_box_slot",
                table: "schedule_blocks",
                columns: new[] { "box_id", "fecha_inicio", "fecha_fin" },
                filter: "is_overbook = true AND overbook_status IN ('Open', 'Offered', 'Occupied')");

            migrationBuilder.Sql(
                """
                UPDATE schedule_blocks
                SET overbook_status = 'Open'
                WHERE is_overbook = true AND overbook_status = 'None';

                UPDATE schedule_blocks
                SET updated_at = NOW()
                WHERE updated_at < '2000-01-01'::timestamptz;

                UPDATE schedule_blocks
                SET concurrency_stamp = id
                WHERE concurrency_stamp = '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenant_confirmation_policies_overbook",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropIndex(
                name: "IX_schedule_blocks_active_overbooks_box_slot",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "auto_overbook_enabled",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "auto_overbook_min_effective_score",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "excluded_clinical_keywords_json",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "excluded_clinical_specialties_json",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "excluded_senior_age_years",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "hitl_overbook_min_effective_score",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "max_false_positive_rate_for_activation",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "max_reschedules_per_appointment",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "max_waiting_list_cascade_offers",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "min_lead_minutes_for_waiting_list_offer",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "min_shadow_evaluations_for_activation",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "waiting_list_offer_ttl_minutes",
                table: "tenant_confirmation_policies");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "deactivation_reason",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "occupied_appointment_id",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "overbook_status",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "response_body_json",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "response_status_code",
                table: "idempotency_keys");
        }
    }
}
