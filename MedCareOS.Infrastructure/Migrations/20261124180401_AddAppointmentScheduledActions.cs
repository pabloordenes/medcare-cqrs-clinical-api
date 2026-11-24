using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentScheduledActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment_scheduled_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    locked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_scheduled_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_appointment_scheduled_actions_citas_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_actions_appointment_status",
                table: "appointment_scheduled_actions",
                columns: new[] { "appointment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_actions_due_claim",
                table: "appointment_scheduled_actions",
                column: "due_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_actions_expired_lease",
                table: "appointment_scheduled_actions",
                column: "lease_until",
                filter: "status = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "UX_scheduled_actions_active_appointment_kind",
                table: "appointment_scheduled_actions",
                columns: new[] { "appointment_id", "kind" },
                unique: true,
                filter: "status IN ('Pending', 'Processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_scheduled_actions");
        }
    }
}
