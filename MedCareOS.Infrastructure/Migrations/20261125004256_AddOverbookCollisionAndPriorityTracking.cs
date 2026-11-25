using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverbookCollisionAndPriorityTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "displaced_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_displaced_by_overbook",
                table: "citas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "offer_accepted_at",
                table: "citas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "waiting_list_offer_id",
                table: "citas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "was_ever_displaced_by_overbook",
                table: "citas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "overbook_collision_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overbook_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    overbook_block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    winner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    winner_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    displaced_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_applied = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_manual_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overridden_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_overbook_collision_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_overbook_collision_events_citas_original_appointment_id",
                        column: x => x.original_appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_overbook_collision_events_citas_overbook_appointment_id",
                        column: x => x.overbook_appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_overbook_collision_events_schedule_blocks_overbook_block_id",
                        column: x => x.overbook_block_id,
                        principalTable: "schedule_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_overbook_collision_events_lookup",
                table: "overbook_collision_events",
                columns: new[] { "original_appointment_id", "overbook_block_id", "trigger_source" });

            migrationBuilder.CreateIndex(
                name: "IX_overbook_collision_events_overbook_appointment_id",
                table: "overbook_collision_events",
                column: "overbook_appointment_id");

            migrationBuilder.CreateIndex(
                name: "IX_overbook_collision_events_overbook_block_id",
                table: "overbook_collision_events",
                column: "overbook_block_id");

            migrationBuilder.CreateIndex(
                name: "IX_overbook_collision_events_tenant_occurred",
                table: "overbook_collision_events",
                columns: new[] { "tenant_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overbook_collision_events");

            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "displaced_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "is_displaced_by_overbook",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "offer_accepted_at",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "waiting_list_offer_id",
                table: "citas");

            migrationBuilder.DropColumn(
                name: "was_ever_displaced_by_overbook",
                table: "citas");
        }
    }
}
