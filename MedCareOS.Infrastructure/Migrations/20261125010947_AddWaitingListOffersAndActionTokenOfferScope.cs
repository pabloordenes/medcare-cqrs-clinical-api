using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingListOffersAndActionTokenOfferScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "cita_id",
                table: "patient_action_tokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "waiting_list_offer_id",
                table: "patient_action_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "waiting_list_offer_id",
                table: "notificaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "waiting_list_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waiting_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cascade_attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    offered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiting_list_offers", x => x.id);
                    table.ForeignKey(
                        name: "FK_waiting_list_offers_citas_result_appointment_id",
                        column: x => x.result_appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_waiting_list_offers_citas_source_appointment_id",
                        column: x => x.source_appointment_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_waiting_list_offers_lista_espera_waiting_list_id",
                        column: x => x.waiting_list_id,
                        principalTable: "lista_espera",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_waiting_list_offers_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_waiting_list_offers_schedule_blocks_schedule_block_id",
                        column: x => x.schedule_block_id,
                        principalTable: "schedule_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patient_action_tokens_waiting_list_offer_id",
                table: "patient_action_tokens",
                column: "waiting_list_offer_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_patient_action_tokens_single_target",
                table: "patient_action_tokens",
                sql: "(cita_id IS NOT NULL AND waiting_list_offer_id IS NULL) OR (cita_id IS NULL AND waiting_list_offer_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_waiting_list_offer_id",
                table: "notificaciones",
                column: "waiting_list_offer_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiting_list_offers_paciente_id",
                table: "waiting_list_offers",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiting_list_offers_pending_expires_at",
                table: "waiting_list_offers",
                columns: new[] { "status", "expires_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_waiting_list_offers_result_appointment_id",
                table: "waiting_list_offers",
                column: "result_appointment_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiting_list_offers_source_appointment_id",
                table: "waiting_list_offers",
                column: "source_appointment_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiting_list_offers_waiting_list_id",
                table: "waiting_list_offers",
                column: "waiting_list_id");

            migrationBuilder.CreateIndex(
                name: "UX_waiting_list_offers_active_block",
                table: "waiting_list_offers",
                column: "schedule_block_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.AddForeignKey(
                name: "FK_notificaciones_waiting_list_offers_waiting_list_offer_id",
                table: "notificaciones",
                column: "waiting_list_offer_id",
                principalTable: "waiting_list_offers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_patient_action_tokens_waiting_list_offers_waiting_list_offe~",
                table: "patient_action_tokens",
                column: "waiting_list_offer_id",
                principalTable: "waiting_list_offers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notificaciones_waiting_list_offers_waiting_list_offer_id",
                table: "notificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_patient_action_tokens_waiting_list_offers_waiting_list_offe~",
                table: "patient_action_tokens");

            migrationBuilder.DropTable(
                name: "waiting_list_offers");

            migrationBuilder.DropIndex(
                name: "IX_patient_action_tokens_waiting_list_offer_id",
                table: "patient_action_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "CK_patient_action_tokens_single_target",
                table: "patient_action_tokens");

            migrationBuilder.DropIndex(
                name: "IX_notificaciones_waiting_list_offer_id",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "waiting_list_offer_id",
                table: "patient_action_tokens");

            migrationBuilder.DropColumn(
                name: "waiting_list_offer_id",
                table: "notificaciones");

            migrationBuilder.AlterColumn<Guid>(
                name: "cita_id",
                table: "patient_action_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
