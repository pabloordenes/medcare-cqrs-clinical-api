using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignKeyRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_trabajadores_usuario_id",
                table: "trabajadores",
                column: "usuario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_blocks_box_id",
                table: "schedule_blocks",
                column: "box_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_blocks_medico_id",
                table: "schedule_blocks",
                column: "medico_id");

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_usuario_id",
                table: "pacientes",
                column: "usuario_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_cita_id",
                table: "notificaciones",
                column: "cita_id");

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_paciente_id",
                table: "notificaciones",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_lista_espera_paciente_id",
                table: "lista_espera",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_citas_paciente_id",
                table: "citas",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "IX_citas_schedule_block_id",
                table: "citas",
                column: "schedule_block_id");

            migrationBuilder.AddForeignKey(
                name: "FK_citas_pacientes_paciente_id",
                table: "citas",
                column: "paciente_id",
                principalTable: "pacientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_citas_schedule_blocks_schedule_block_id",
                table: "citas",
                column: "schedule_block_id",
                principalTable: "schedule_blocks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lista_espera_pacientes_paciente_id",
                table: "lista_espera",
                column: "paciente_id",
                principalTable: "pacientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notificaciones_citas_cita_id",
                table: "notificaciones",
                column: "cita_id",
                principalTable: "citas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_notificaciones_pacientes_paciente_id",
                table: "notificaciones",
                column: "paciente_id",
                principalTable: "pacientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_pacientes_usuarios_usuario_id",
                table: "pacientes",
                column: "usuario_id",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_schedule_blocks_boxes_box_id",
                table: "schedule_blocks",
                column: "box_id",
                principalTable: "boxes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_schedule_blocks_trabajadores_medico_id",
                table: "schedule_blocks",
                column: "medico_id",
                principalTable: "trabajadores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_trabajadores_usuarios_usuario_id",
                table: "trabajadores",
                column: "usuario_id",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_citas_pacientes_paciente_id",
                table: "citas");

            migrationBuilder.DropForeignKey(
                name: "FK_citas_schedule_blocks_schedule_block_id",
                table: "citas");

            migrationBuilder.DropForeignKey(
                name: "FK_lista_espera_pacientes_paciente_id",
                table: "lista_espera");

            migrationBuilder.DropForeignKey(
                name: "FK_notificaciones_citas_cita_id",
                table: "notificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_notificaciones_pacientes_paciente_id",
                table: "notificaciones");

            migrationBuilder.DropForeignKey(
                name: "FK_pacientes_usuarios_usuario_id",
                table: "pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_schedule_blocks_boxes_box_id",
                table: "schedule_blocks");

            migrationBuilder.DropForeignKey(
                name: "FK_schedule_blocks_trabajadores_medico_id",
                table: "schedule_blocks");

            migrationBuilder.DropForeignKey(
                name: "FK_trabajadores_usuarios_usuario_id",
                table: "trabajadores");

            migrationBuilder.DropIndex(
                name: "IX_trabajadores_usuario_id",
                table: "trabajadores");

            migrationBuilder.DropIndex(
                name: "IX_schedule_blocks_box_id",
                table: "schedule_blocks");

            migrationBuilder.DropIndex(
                name: "IX_schedule_blocks_medico_id",
                table: "schedule_blocks");

            migrationBuilder.DropIndex(
                name: "IX_pacientes_usuario_id",
                table: "pacientes");

            migrationBuilder.DropIndex(
                name: "IX_notificaciones_cita_id",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_notificaciones_paciente_id",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_lista_espera_paciente_id",
                table: "lista_espera");

            migrationBuilder.DropIndex(
                name: "IX_citas_paciente_id",
                table: "citas");

            migrationBuilder.DropIndex(
                name: "IX_citas_schedule_block_id",
                table: "citas");
        }
    }
}
