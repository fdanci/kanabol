using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "number",
                table: "task_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Tasks made before numbers existed get 1, 2, 3... in each project, in the order they were
            // made, and each project's counter goes on after its last one. Archived tasks count too:
            // numbers aren't reused.
            migrationBuilder.Sql("""
                UPDATE task_items AS task
                SET number = numbered.n
                FROM (SELECT id, row_number() OVER (PARTITION BY project_id ORDER BY created_at, id)::int AS n FROM task_items) AS numbered
                WHERE task.id = numbered.id;
                """);
            migrationBuilder.Sql("""
                UPDATE projects AS project
                SET next_task_number = counted.last + 1
                FROM (SELECT project_id, max(number) AS last FROM task_items GROUP BY project_id) AS counted
                WHERE project.id = counted.project_id;
                """);

            // Only for the rows above: a new task always comes with its number.
            migrationBuilder.Sql("ALTER TABLE task_items ALTER COLUMN number DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ux_task_items_project_id_number",
                table: "task_items",
                columns: new[] { "project_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_task_items_project_id_number",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "number",
                table: "task_items");
        }
    }
}
