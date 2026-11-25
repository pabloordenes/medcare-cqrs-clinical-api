using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientSelfRescheduleHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "citas",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_rescheduled_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reschedules_count",
                table: "citas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "appointment_reschedule_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_schedule_block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_schedule_block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    was_displaced_priority = table.Column<bool>(type: "boolean", nullable: false),
                    reschedule_number = table.Column<int>(type: "integer", nullable: false),
                    rescheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_reschedule_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_appointment_reschedule_history_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointment_reschedule_history_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointment_reschedule_history_schedule_blocks_from_schedul~",
                        column: x => x.from_schedule_block_id,
                        principalTable: "schedule_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointment_reschedule_history_schedule_blocks_to_schedule_~",
                        column: x => x.to_schedule_block_id,
                        principalTable: "schedule_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reschedule_history_appointment_id",
                table: "appointment_reschedule_history",
                column: "cita_id");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reschedule_history_from_schedule_block_id",
                table: "appointment_reschedule_history",
                column: "from_schedule_block_id");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reschedule_history_paciente_id",
                table: "appointment_reschedule_history",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_appointment_reschedule_history_to_schedule_block_id",
                table: "appointment_reschedule_history",
                column: "to_schedule_block_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_reschedule_history");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "last_rescheduled_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "reschedules_count",
                table: "citas");
        }
    }
}
