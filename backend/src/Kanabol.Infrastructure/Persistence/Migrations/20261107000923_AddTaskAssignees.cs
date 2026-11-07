using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAssignees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_assignees",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_assignees", x => new { x.task_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_task_assignees_task_items_task_id",
                        column: x => x.task_id,
                        principalTable: "task_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_assignees_users_assigned_by_id",
                        column: x => x.assigned_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_assignees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_assigned_by_id",
                table: "task_assignees",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_assignees_user_id",
                table: "task_assignees",
                column: "user_id");

            // Each assignee so far stays, assigned when the task last changed, or was made, by nobody
            // known (SPEC section 5.1). Archived tasks keep theirs too.
            migrationBuilder.Sql("""
                INSERT INTO task_assignees (task_id, user_id, assigned_at, assigned_by_id)
                SELECT id, assignee_id, coalesce(updated_at, created_at), NULL
                FROM task_items
                WHERE assignee_id IS NOT NULL;
                """);

            // The history said AssigneeId, one id or none: as a list of one, or an empty one, it reads
            // like what Assign writes from now on.
            migrationBuilder.Sql("""
                UPDATE task_history_entries
                SET field = 'Assignees', old_value = coalesce(old_value, ''), new_value = coalesce(new_value, '')
                WHERE field = 'AssigneeId';
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_users_assignee_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_assignee_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "task_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_assignee_id",
                table: "task_items",
                column: "assignee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_users_assignee_id",
                table: "task_items",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // One assignee per task, the first one assigned. Entries of one id or none go back to AssigneeId,
            // and those of several stay as they are, since one column can't hold them.
            migrationBuilder.Sql("""
                UPDATE task_items AS task
                SET assignee_id = earliest.user_id
                FROM (
                    SELECT DISTINCT ON (task_id) task_id, user_id
                    FROM task_assignees
                    ORDER BY task_id, assigned_at, user_id
                ) AS earliest
                WHERE task.id = earliest.task_id;
                """);
            migrationBuilder.Sql("""
                UPDATE task_history_entries
                SET field = 'AssigneeId', old_value = nullif(old_value, ''), new_value = nullif(new_value, '')
                WHERE field = 'Assignees' AND position(',' in old_value) = 0 AND position(',' in new_value) = 0;
                """);

            migrationBuilder.DropTable(
                name: "task_assignees");
        }
    }
}
