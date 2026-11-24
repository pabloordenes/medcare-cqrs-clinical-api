using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientNotificationConsentAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "canal_preferido",
                table: "pacientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opt_out_at",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "opt_out_source",
                table: "pacientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "whatsapp_opt_in",
                table: "pacientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "whatsapp_opt_in_at",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whatsapp_opt_in_source",
                table: "pacientes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pacientes_telefono",
                table: "pacientes",
                column: "telefono");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pacientes_telefono",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "canal_preferido",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "opt_out_at",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "opt_out_source",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "whatsapp_opt_in",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "whatsapp_opt_in_at",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "whatsapp_opt_in_source",
                table: "pacientes");
        }
    }
}
