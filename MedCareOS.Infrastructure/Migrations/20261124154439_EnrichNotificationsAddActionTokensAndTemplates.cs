using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnrichNotificationsAddActionTokensAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notificaciones_cita_id",
                table: "notificaciones");

            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                table: "notificaciones",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "estado",
                table: "notificaciones",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "contenido",
                table: "notificaciones",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "cita_id",
                table: "notificaciones",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "canal",
                table: "notificaciones",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "notificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "canal_original",
                table: "notificaciones",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "content_redacted_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                table: "notificaciones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "notificaciones",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "locale",
                table: "notificaciones",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "es-CL");

            migrationBuilder.AddColumn<string>(
                name: "provider_message_id",
                table: "notificaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_name",
                table: "notificaciones",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sent_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_key",
                table: "notificaciones",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "appointment.created");

            migrationBuilder.AddColumn<string>(
                name: "template_params_json",
                table: "notificaciones",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "template_version",
                table: "notificaciones",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "notificaciones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "notificaciones",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "notification_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    subject_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body_template = table.Column<string>(type: "text", nullable: false),
                    meta_template_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patient_action_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cita_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notificacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_action_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_patient_action_tokens_citas_cita_id",
                        column: x => x.cita_id,
                        principalTable: "citas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_action_tokens_notificaciones_notificacion_id",
                        column: x => x.notificacion_id,
                        principalTable: "notificaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_patient_action_tokens_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE notificaciones
                SET
                    estado = CASE
                        WHEN LOWER(TRIM(estado)) = 'pendiente' THEN 'Pending'
                        WHEN estado IN ('Pending', 'Scheduled', 'Sent', 'Delivered', 'Read', 'Failed', 'Cancelled') THEN estado
                        ELSE 'Pending'
                    END,
                    canal = CASE
                        WHEN LOWER(canal) LIKE '%email%' THEN 'Email'
                        WHEN LOWER(canal) LIKE '%whatsapp%' OR LOWER(canal) LIKE '%tel%' THEN 'WhatsApp'
                        WHEN canal IN ('WhatsApp', 'Email') THEN canal
                        ELSE 'Email'
                    END,
                    tipo = CASE
                        WHEN tipo IN ('AppointmentCreated', 'Reminder', 'FinalConfirmation', 'OverbookNotice', 'WaitingListOffer', 'OverbookOfferCancelled', 'RescheduleHelp') THEN tipo
                        ELSE 'AppointmentCreated'
                    END,
                    scheduled_for = CASE
                        WHEN scheduled_for = '0001-01-01 00:00:00+00'::timestamptz THEN COALESCE("CreatedAt", NOW())
                        ELSE scheduled_for
                    END,
                    updated_at = CASE
                        WHEN updated_at = '0001-01-01 00:00:00+00'::timestamptz THEN COALESCE("CreatedAt", NOW())
                        ELSE updated_at
                    END,
                    correlation_id = CASE
                        WHEN correlation_id = '00000000-0000-0000-0000-000000000000'::uuid THEN id
                        ELSE correlation_id
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_retention",
                table: "notificaciones",
                columns: new[] { "content_redacted_at", "CreatedAt" },
                filter: "content_redacted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_scheduled",
                table: "notificaciones",
                columns: new[] { "estado", "scheduled_for" },
                filter: "estado = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "UX_notificaciones_idempotency",
                table: "notificaciones",
                columns: new[] { "cita_id", "tipo", "scheduled_for" },
                unique: true,
                filter: "cita_id IS NOT NULL AND estado <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "UX_notificaciones_provider_message_id",
                table: "notificaciones",
                column: "provider_message_id",
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_notification_templates_key",
                table: "notification_templates",
                columns: new[] { "tenant_id", "template_key", "channel", "locale", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_action_tokens_cita_id",
                table: "patient_action_tokens",
                column: "cita_id");

            migrationBuilder.CreateIndex(
                name: "IX_patient_action_tokens_expires_at",
                table: "patient_action_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_patient_action_tokens_notificacion_id",
                table: "patient_action_tokens",
                column: "notificacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_patient_action_tokens_paciente_id",
                table: "patient_action_tokens",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "UX_patient_action_tokens_hash",
                table: "patient_action_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_templates");

            migrationBuilder.DropTable(
                name: "patient_action_tokens");

            migrationBuilder.DropIndex(
                name: "IX_notificaciones_retention",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "IX_notificaciones_scheduled",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "UX_notificaciones_idempotency",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "UX_notificaciones_provider_message_id",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "canal_original",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "content_redacted_at",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "error",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "locale",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "provider_message_id",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "provider_name",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "read_at",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "sent_at",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "template_key",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "template_params_json",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "notificaciones");

            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                table: "notificaciones",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "estado",
                table: "notificaciones",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "contenido",
                table: "notificaciones",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "cita_id",
                table: "notificaciones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "canal",
                table: "notificaciones",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_cita_id",
                table: "notificaciones",
                column: "cita_id");
        }
    }
}
