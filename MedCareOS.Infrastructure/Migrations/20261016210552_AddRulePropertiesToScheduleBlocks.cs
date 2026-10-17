using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRulePropertiesToScheduleBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "rule_days_of_week",
                table: "schedule_blocks",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<DateOnly>(
                name: "rule_end_date",
                table: "schedule_blocks",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "rule_end_time",
                table: "schedule_blocks",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<Guid>(
                name: "rule_group_id",
                table: "schedule_blocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateOnly>(
                name: "rule_start_date",
                table: "schedule_blocks",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "rule_start_time",
                table: "schedule_blocks",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rule_days_of_week",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "rule_end_date",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "rule_end_time",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "rule_group_id",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "rule_start_date",
                table: "schedule_blocks");

            migrationBuilder.DropColumn(
                name: "rule_start_time",
                table: "schedule_blocks");
        }
    }
}
