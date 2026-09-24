using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    capacidad = table.Column<int>(type: "integer", nullable: false),
                    piso = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "citas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    sintomas_texto = table.Column<string>(type: "text", nullable: false),
                    sintomas_procesados = table.Column<string>(type: "jsonb", nullable: true),
                    resumen_nlp = table.Column<string>(type: "text", nullable: true),
                    score_riesgo = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    banda_riesgo = table.Column<string>(type: "text", nullable: true),
                    fuente_agendamiento = table.Column<string>(type: "text", nullable: true),
                    asistio = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_citas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lista_espera",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    especialidad = table.Column<string>(type: "text", nullable: false),
                    fecha_inscripcion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    prioridad = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    contexto_clinico = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lista_espera", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    canal = table.Column<string>(type: "text", nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificaciones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pacientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rut = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    apellido = table.Column<string>(type: "text", nullable: false),
                    fecha_nacimiento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    genero = table.Column<string>(type: "text", nullable: false),
                    direccion = table.Column<string>(type: "text", nullable: true),
                    barrio = table.Column<string>(type: "text", nullable: true),
                    telefono = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pacientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    box_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fecha_fin = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_blocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trabajadores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    apellido = table.Column<string>(type: "text", nullable: false),
                    rut = table.Column<string>(type: "text", nullable: false),
                    especialidad = table.Column<string>(type: "text", nullable: true),
                    cargo = table.Column<string>(type: "text", nullable: false),
                    telefono = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trabajadores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    rol = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_rut",
                table: "pacientes",
                column: "rut",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trabajadores_rut",
                table: "trabajadores",
                column: "rut",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boxes");

            migrationBuilder.DropTable(
                name: "citas");

            migrationBuilder.DropTable(
                name: "lista_espera");

            migrationBuilder.DropTable(
                name: "notificaciones");

            migrationBuilder.DropTable(
                name: "pacientes");

            migrationBuilder.DropTable(
                name: "schedule_blocks");

            migrationBuilder.DropTable(
                name: "trabajadores");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
