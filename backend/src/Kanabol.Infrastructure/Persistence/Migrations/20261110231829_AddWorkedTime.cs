using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkedTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "work_started_at",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "worked_seconds",
                table: "task_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // SPEC section 4.4: the counter runs while a task is in În lucru and not archived. Nothing tells when
            // the open tasks already there came in, so they start counting now and have worked nothing before.
            migrationBuilder.Sql("""
                UPDATE task_items SET work_started_at = now()
                WHERE archived_at IS NULL
                  AND column_id IN (SELECT id FROM board_columns WHERE kind = 'InProgress');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "work_started_at",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "worked_seconds",
                table: "task_items");
        }
    }
}
