using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOverbookPropertiesToScheduleBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "rule_start_time",
                table: "schedule_blocks",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "rule_start_date",
                table: "schedule_blocks",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<Guid>(
                name: "rule_group_id",
                table: "schedule_blocks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "rule_end_time",
                table: "schedule_blocks",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "rule_end_date",
                table: "schedule_blocks",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<int[]>(
                name: "rule_days_of_week",
                table: "schedule_blocks",
                type: "integer[]",
                nullable: true,
                oldClrType: typeof(int[]),
                oldType: "integer[]");

            migrationBuilder.AddColumn<bool>(
                name: "is_overbook",
                table: "schedule_blocks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "source_appointment_id",
                table: "schedule_blocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_blocks_source_appointment_id",
                table: "schedule_blocks",
                column: "source_appointment_id",
                unique: true,
                filter: "source_appointment_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_schedule_blocks_citas_source_appointment_id",
                table: "schedule_blocks",
                column: "source_appointment_id",
                principalTable: "citas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_schedule_blocks_citas_source_appointment_id",
                table: "schedule_blocks");

            migrationBuilder.DropIndex(
                name: "IX_schedule_blocks_source_appointment_id",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "is_overbook",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "source_appointment_id",
                table: "schedule_blocks");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "rule_start_time",
                table: "schedule_blocks",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "rule_start_date",
                table: "schedule_blocks",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "rule_group_id",
                table: "schedule_blocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "rule_end_time",
                table: "schedule_blocks",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "rule_end_date",
                table: "schedule_blocks",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<int[]>(
                name: "rule_days_of_week",
                table: "schedule_blocks",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0],
                oldClrType: typeof(int[]),
                oldType: "integer[]",
                oldNullable: true);
        }
    }
}
