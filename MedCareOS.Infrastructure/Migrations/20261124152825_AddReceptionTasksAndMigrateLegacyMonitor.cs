using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceptionTasksAndMigrateLegacyMonitor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reception_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_tarea = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    prioridad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    asignado_a_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notas_resolucion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resuelto_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reception_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_reception_tasks_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reception_tasks_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reception_tasks_usuarios_asignado_a_usuario_id",
                        column: x => x.asignado_a_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_reception_tasks_usuarios_resuelto_por_usuario_id",
                        column: x => x.resuelto_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reception_tasks_asignado_a_usuario_id",
                table: "reception_tasks",
                column: "asignado_a_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_reception_tasks_paciente_id",
                table: "reception_tasks",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_reception_tasks_resuelto_por_usuario_id",
                table: "reception_tasks",
                column: "resuelto_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_reception_tasks_status_priority_created",
                table: "reception_tasks",
                columns: new[] { "estado", "prioridad", "created_at" });

            migrationBuilder.CreateIndex(
                name: "UX_reception_tasks_active_appointment_type",
                table: "reception_tasks",
                columns: new[] { "cita_id", "tipo_tarea" },
                unique: true,
                filter: "estado IN ('Open', 'InProgress')");

            migrationBuilder.Sql("""
                INSERT INTO reception_tasks (id, cita_id, paciente_id, tipo_tarea, prioridad, motivo, estado, created_at, updated_at)
                SELECT id, cita_id, paciente_id, 'ManualConfirmation', 'High', LEFT(COALESCE(NULLIF(TRIM(contenido), ''), 'Confirmación manual requerida'), 500), 'Open', "CreatedAt", "CreatedAt"
                FROM notificaciones
                WHERE tipo = 'Confirmación Manual'
                ON CONFLICT DO NOTHING;

                DELETE FROM notificaciones WHERE tipo = 'Confirmación Manual';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reception_tasks");
        }
    }
}
