using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeWaitingListModuleSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "estado",
                table: "lista_espera",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "especialidad",
                table: "lista_espera",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "lista_espera",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "lista_espera",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "displaced_source_appointment_id",
                table: "lista_espera",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "especialidad_normalizada",
                table: "lista_espera",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "lista_espera",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_displaced_bonus",
                table: "lista_espera",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "preferred_from_date",
                table: "lista_espera",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preferred_staff_id",
                table: "lista_espera",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "preferred_time_end",
                table: "lista_espera",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "preferred_time_start",
                table: "lista_espera",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "preferred_to_date",
                table: "lista_espera",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "lista_espera",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "lista_espera",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(
                """
                UPDATE lista_espera
                SET tenant_id = '11111111-1111-1111-1111-111111111111'
                WHERE tenant_id = '00000000-0000-0000-0000-000000000000';

                UPDATE lista_espera
                SET estado = CASE LOWER(estado)
                    WHEN 'activa' THEN 'Active'
                    WHEN 'active' THEN 'Active'
                    WHEN 'ofertada' THEN 'Offered'
                    WHEN 'offered' THEN 'Offered'
                    WHEN 'aceptada' THEN 'Accepted'
                    WHEN 'accepted' THEN 'Accepted'
                    WHEN 'expirada' THEN 'Expired'
                    WHEN 'expired' THEN 'Expired'
                    WHEN 'cancelada' THEN 'Cancelled'
                    WHEN 'cancelled' THEN 'Cancelled'
                    ELSE estado
                END;

                UPDATE lista_espera
                SET especialidad_normalizada = LOWER(TRIM(especialidad))
                WHERE especialidad_normalizada = '';

                UPDATE lista_espera
                SET updated_at = COALESCE(fecha_inscripcion, NOW())
                WHERE updated_at < '2000-01-01'::timestamptz;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_lista_espera_displaced_source_appointment_id",
                table: "lista_espera",
                column: "displaced_source_appointment_id");

            migrationBuilder.CreateIndex(
                name: "IX_lista_espera_preferred_staff_id",
                table: "lista_espera",
                column: "preferred_staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_lista_espera_tenant_specialty_status",
                table: "lista_espera",
                columns: new[] { "tenant_id", "especialidad_normalizada", "estado" });

            migrationBuilder.CreateIndex(
                name: "UX_lista_espera_active_patient_specialty",
                table: "lista_espera",
                columns: new[] { "tenant_id", "paciente_id", "especialidad_normalizada" },
                unique: true,
                filter: "estado IN ('Active', 'Offered')");

            migrationBuilder.AddForeignKey(
                name: "FK_lista_espera_citas_displaced_source_appointment_id",
                table: "lista_espera",
                column: "displaced_source_appointment_id",
                principalTable: "citas",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_lista_espera_trabajadores_preferred_staff_id",
                table: "lista_espera",
                column: "preferred_staff_id",
                principalTable: "trabajadores",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lista_espera_citas_displaced_source_appointment_id",
                table: "lista_espera");

            migrationBuilder.DropForeignKey(
                name: "FK_lista_espera_trabajadores_preferred_staff_id",
                table: "lista_espera");

            migrationBuilder.DropIndex(
                name: "IX_lista_espera_displaced_source_appointment_id",
                table: "lista_espera");

            migrationBuilder.DropIndex(
                name: "IX_lista_espera_preferred_staff_id",
                table: "lista_espera");

            migrationBuilder.DropIndex(
                name: "IX_lista_espera_tenant_specialty_status",
                table: "lista_espera");

            migrationBuilder.DropIndex(
                name: "UX_lista_espera_active_patient_specialty",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "displaced_source_appointment_id",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "especialidad_normalizada",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "is_displaced_bonus",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "preferred_from_date",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "preferred_staff_id",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "preferred_time_end",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "preferred_time_start",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "preferred_to_date",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "lista_espera");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "lista_espera");

            migrationBuilder.AlterColumn<string>(
                name: "estado",
                table: "lista_espera",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "especialidad",
                table: "lista_espera",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);
        }
    }
}
