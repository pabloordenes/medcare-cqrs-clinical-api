using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedCareOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveAppointmentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_citas_schedule_block_id\";");

            // Cancel pre-existing duplicate active appointments on the same block before creating the unique index.
            migrationBuilder.Sql("""
                WITH ranked_appointments AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY schedule_block_id
                            ORDER BY
                                CASE estado
                                    WHEN 'Finished' THEN 1
                                    WHEN 'InProgress' THEN 2
                                    WHEN 'Confirmed' THEN 3
                                    WHEN 'Scheduled' THEN 4
                                    ELSE 5
                                END ASC,
                                "CreatedAt" ASC,
                                id ASC
                        ) AS rn
                    FROM citas
                    WHERE estado <> 'Cancelled'
                      AND schedule_block_id IS NOT NULL
                )
                UPDATE citas c
                SET estado = 'Cancelled',
                    "UpdatedAt" = NOW()
                FROM ranked_appointments r
                WHERE c.id = r.id
                  AND r.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_citas_schedule_block_id",
                table: "citas",
                column: "schedule_block_id",
                unique: true,
                filter: "estado <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_citas_schedule_block_id",
                table: "citas");

            migrationBuilder.CreateIndex(
                name: "IX_citas_schedule_block_id",
                table: "citas",
                column: "schedule_block_id");
        }
    }
}
