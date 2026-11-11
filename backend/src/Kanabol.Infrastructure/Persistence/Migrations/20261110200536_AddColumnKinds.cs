using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kanabol.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "board_columns",
                type: "text",
                nullable: false,
                defaultValue: "Custom");

            // Each project gets the four fixed columns (SPEC section 4.4, "Coloanele fixe"): the column at
            // position 0 becomes Backlog; the first done column from the left after it becomes Gata and goes
            // last; another column named "În lucru", in any case, becomes it, or a new one comes right after
            // Backlog; a new "În pauză" comes right after "În lucru"; a project with no other done column
            // gets a new Gata at the end. The fixed ones take their names, the project's own keep theirs, and
            // no task changes its column. The tasks finished outside Gata are open again, archived ones apart,
            // which keep their moment as a column that stopped being done always left them. New ids are
            // UUID v7, as the API makes them.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    project record;
                    backlog uuid;
                    done uuid;
                    progress uuid;
                    progress_place int;
                BEGIN
                    FOR project IN SELECT id FROM projects LOOP
                        SELECT id INTO backlog FROM board_columns WHERE project_id = project.id ORDER BY position LIMIT 1;
                        IF backlog IS NULL THEN
                            backlog := encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid()) placing substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3) FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid;
                            INSERT INTO board_columns (id, project_id, name, position, is_done, kind, created_at)
                            VALUES (backlog, project.id, 'Backlog', 0, false, 'Backlog', now());
                        ELSE
                            UPDATE board_columns SET kind = 'Backlog', name = 'Backlog' WHERE id = backlog;
                        END IF;

                        SELECT id INTO done FROM board_columns
                        WHERE project_id = project.id AND is_done AND id <> backlog ORDER BY position LIMIT 1;

                        SELECT id INTO progress FROM board_columns
                        WHERE project_id = project.id AND id <> backlog AND id IS DISTINCT FROM done
                            AND lower(name COLLATE "ro-x-icu") = 'în lucru'
                        ORDER BY position LIMIT 1;
                        IF progress IS NULL THEN
                            UPDATE board_columns SET position = position + 1 WHERE project_id = project.id AND id <> backlog;
                            progress := encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid()) placing substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3) FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid;
                            INSERT INTO board_columns (id, project_id, name, position, is_done, kind, created_at)
                            VALUES (progress, project.id, 'În lucru', 1, false, 'InProgress', now());
                        ELSE
                            UPDATE board_columns SET kind = 'InProgress', name = 'În lucru' WHERE id = progress;
                        END IF;

                        SELECT position INTO progress_place FROM board_columns WHERE id = progress;
                        UPDATE board_columns SET position = position + 1 WHERE project_id = project.id AND position > progress_place;
                        INSERT INTO board_columns (id, project_id, name, position, is_done, kind, created_at)
                        VALUES (encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid()) placing substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3) FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid, project.id, 'În pauză', progress_place + 1, false, 'Paused', now());

                        IF done IS NULL THEN
                            INSERT INTO board_columns (id, project_id, name, position, is_done, kind, created_at)
                            SELECT encode(set_bit(set_bit(overlay(uuid_send(gen_random_uuid()) placing substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3) FROM 1 FOR 6), 52, 1), 53, 1), 'hex')::uuid, project.id, 'Gata', max(position) + 1, true, 'Done', now()
                            FROM board_columns WHERE project_id = project.id;
                        ELSE
                            UPDATE board_columns SET kind = 'Done', name = 'Gata',
                                position = (SELECT max(position) + 1 FROM board_columns WHERE project_id = project.id)
                            WHERE id = done;
                        END IF;

                        UPDATE board_columns AS col SET position = ordered.place
                        FROM (SELECT id, row_number() OVER (ORDER BY position) - 1 AS place FROM board_columns WHERE project_id = project.id) AS ordered
                        WHERE col.id = ordered.id AND col.position <> ordered.place;
                    END LOOP;

                    UPDATE task_items AS task SET completed_at = NULL
                    FROM board_columns AS col
                    WHERE col.id = task.column_id AND col.kind <> 'Done' AND task.completed_at IS NOT NULL AND task.archived_at IS NULL;
                END $$;
                ALTER TABLE board_columns ALTER COLUMN kind DROP DEFAULT;
                """);

            migrationBuilder.DropColumn(
                name: "is_done",
                table: "board_columns");

            migrationBuilder.CreateIndex(
                name: "ux_board_columns_project_id_kind",
                table: "board_columns",
                columns: new[] { "project_id", "kind" },
                unique: true,
                filter: "kind <> 'Custom'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_board_columns_project_id_kind",
                table: "board_columns");

            migrationBuilder.AddColumn<bool>(
                name: "is_done",
                table: "board_columns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Gata is the done column again; the columns stay as they are.
            migrationBuilder.Sql("UPDATE board_columns SET is_done = true WHERE kind = 'Done';");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "board_columns");
        }
    }
}
