using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "estimated_minutes",
                table: "task_items",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estimated_minutes",
                table: "task_items");
        }
    }
}
