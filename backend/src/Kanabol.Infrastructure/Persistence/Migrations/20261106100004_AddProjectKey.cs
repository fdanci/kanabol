using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key",
                table: "projects",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "next_task_number",
                table: "projects",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // A project made before keys existed gets one of its own, P and four letters, so the index can be unique.
            migrationBuilder.Sql("""
                UPDATE projects AS project
                SET key = 'P' || chr(65 + (n / 17576) % 26) || chr(65 + (n / 676) % 26) || chr(65 + (n / 26) % 26) || chr(65 + n % 26)
                FROM (SELECT id, (row_number() OVER (ORDER BY created_at, id) - 1)::int AS n FROM projects) AS numbered
                WHERE project.id = numbered.id
                """);

            migrationBuilder.CreateIndex(
                name: "ux_projects_key",
                table: "projects",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_projects_key",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "key",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "next_task_number",
                table: "projects");
        }
    }
}
