# Backend

.NET 10 API with controllers, PostgreSQL 17 and EF Core.

## Commands

Run from `backend/`:

- Build: `dotnet build Kanabol.sln`
- Run: `dotnet run --project src/Kanabol.Api --launch-profile http` (serves on http://localhost:5200)
- Lint: `dotnet build Kanabol.sln -warnaserror` (analyzers; add `--no-incremental` if nothing changed since the last build)
- Check formatting: `dotnet format Kanabol.sln --verify-no-changes`
- Test: `dotnet test Kanabol.sln`, with Docker Desktop running: the tests start their own PostgreSQL 17 in a container, so they don't need the local database.
- Test back and front together, from the repo root: `scripts/test-all.sh`
- Tests in `Kanabol.Api.Tests` can read what a real request logged through `Api.Factory.Logs`.
- Run the structure checks, from the repo root: `scripts/check-backend.sh [task-number...]`. They need a PostgreSQL whose user may create databases, and each run works in its own databases and drops them. The connection string comes from `KANABOL_CHECK_DB`, or else from the `ConnectionStrings:Default` user secret.
- Run the authentication checks, from the repo root: `scripts/check-auth.sh [task-number...]`. They run the auth tests, and start the real API on a database of their own, under the same conditions as the structure checks.
- Run the permission checks, from the repo root: `scripts/check-permissions.sh [task-number...]`, under the same conditions.
- Run the user and role checks, from the repo root: `scripts/check-users.sh [task-number...]`, under the same conditions.
- Run the project checks, from the repo root: `scripts/check-projects.sh [task-number...]`, under the same conditions.
- Run the task checks, from the repo root: `scripts/check-tasks.sh [task-number...]`, under the same conditions, those of the board: `scripts/check-board.sh [task-number...]`, and those of several assignees: `scripts/check-assignees.sh [task-number...]`.
- Run the real-time checks, from the repo root: `scripts/check-realtime.sh [task-number...]`, under the same conditions. They reach the hub over a real WebSocket, with a small SignalR client in Node.
- Run the checks of comments and attachments, from the repo root: `scripts/check-comments.sh [task-number...]`, under the same conditions.
- Run the checks of a task's history, from the repo root: `scripts/check-history.sh [task-number...]`, under the same conditions; 2 and 3 are the frontend's, and need only the packages, and 6, a description's texts and their diff, needs both.
- Run the checks of the deadline alerts, from the repo root: `scripts/check-alerts.sh [task-number...]`, under the same conditions.
- Run the checks of the main panel and "Task-urile mele", from the repo root: `scripts/check-dashboard.sh [task-number...]`, under the same conditions.

## Migrations

Run `dotnet tool restore` once after cloning, which installs the `dotnet-ef` version the repo pins. Then, from `backend/`:

- Add one: `dotnet ef migrations add <Name> -p src/Kanabol.Infrastructure -s src/Kanabol.Api -o Persistence/Migrations`
- Read its SQL before committing: `dotnet ef migrations script -p src/Kanabol.Infrastructure -s src/Kanabol.Api`
- Apply them by hand: `dotnet ef database update -p src/Kanabol.Infrastructure -s src/Kanabol.Api`

The API applies them itself on startup while `Database:MigrateOnStartup` is true, which is the default.

## Local database

The API needs a PostgreSQL 17 on `localhost:5432`, with a `kanabol` user that may create databases: the first migration creates the `kanabol` database. Without Seq, the API still runs and drops its logs for Seq.

`docker compose up postgres seq` from the repo root provides both PostgreSQL and Seq. Before the first time, copy `.env.example` to `.env` and fill it in, with the same database password as in the `ConnectionStrings:Default` secret below. Nothing else may hold port 5432.

Seq's UI is on http://localhost:5341. Each new Seq volume needs its retention set once: `SEQ_ADMIN_PASSWORD=<password> scripts/seq-retention.sh`, which makes Seq delete logs after 30 days. The password is the one you hashed for `SEQ_ADMIN_HASH`.

Set these once per machine, from `backend/`:

- `dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=kanabol;Username=kanabol;Password=<password>" --project src/Kanabol.Api`
- `dotnet user-secrets set "Jwt:Secret" "<at least 32 characters>" --project src/Kanabol.Api`
- `dotnet user-secrets set "Admin:InitialPassword" "<at least 10 characters>" --project src/Kanabol.Api`

The first start creates the permissions, the `Admin` role and the admin user `admin@kanabol.local`, who has to change that password at first login. Later starts don't need the password, and they bring the permissions in line with `Permissions.All` in the code.

## Configuration

- Shared values are in `appsettings.json`, and values for one environment in `appsettings.Development.json` and `appsettings.Production.json`.
- Development secrets go in user secrets, never in a file.
- Local overrides that aren't secret go in `src/Kanabol.Api/appsettings.Development.local.json`, which git ignores. It's only read in Development, and it wins over everything else.
- The API refuses to start if `Jwt:Secret` (at least 32 characters), `Storage:Root` or the connection string is missing, if a new database has no `Admin:InitialPassword` (at least 10 characters), or if `Alerts:OverdueCheckInterval` is under a minute or over a day.

In Docker and production, settings come from environment variables:

| Variable                               | Value                                                                                                                                 |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`               | `Production` unless set                                                                                                               |
| `ConnectionStrings__Default`           | The PostgreSQL connection string                                                                                                      |
| `Jwt__Secret`                          | The key that signs JWTs                                                                                                               |
| `Admin__InitialPassword`               | The first admin's password, needed on the first start only                                                                            |
| `Storage__Root`                        | The attachments folder, `/data/attachments` by default                                                                                |
| `Storage__AllowedExtensions`           | The extensions a file may have, one value separated by commas, `pdf,png`, which replaces the whole default list in `appsettings.json` |
| `Database__MigrateOnStartup`           | `true` by default                                                                                                                     |
| `Alerts__OverdueCheckInterval`         | How often the API looks for overdue tasks, `00:15:00` by default, at least `00:01:00` and at most a day                               |
| `Alerts__OverdueCheckEnabled`          | `true` by default; `false` stops the overdue check, as the API's tests do                                                             |
| `Serilog__WriteTo__1__Args__serverUrl` | Seq's address, `http://seq:5341` by default                                                                                           |

## Access rules

- Code checks permissions, never role names.
- Visibility across all projects comes from the global `projects.view_all` permission, not from a role.
- Access is checked on every request. The frontend only hides what isn't available.
- `[HasPermission(Permissions.X)]` on an action or a controller closes it to tokens without that permission, with a 403 `Auth.PermissionRequired`. Several of them all have to hold.
- `IProjectAccess` answers from the database, about the user it's asked about, and refuses deactivated users. It doesn't check that the project exists, so a handler loads the project or task first.
- Tests reach paths no real endpoint has yet through sample controllers in the test project. `scripts/check-permissions.sh` makes sure they aren't in what ships.

## Users and roles

| Endpoint                                                                                                    | Does                                                                                                                                                                | Needs          |
| ----------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------- |
| `GET /api/roles`, `GET /api/roles/{id}`, `POST /api/roles`, `PUT /api/roles/{id}`, `DELETE /api/roles/{id}` | Roles and their permissions                                                                                                                                         | `roles.manage` |
| `GET /api/permissions`                                                                                      | Every permission that exists                                                                                                                                        | `roles.manage` |
| `GET /api/users`, `GET /api/users/{id}`, `POST /api/users`, `PUT /api/users/{id}`                           | Users, with an initial password on creation                                                                                                                         | `users.manage` |
| `GET /api/users/assignable-roles`                                                                           | The roles this caller can give                                                                                                                                      | `users.manage` |
| `PUT /api/users/{id}/role`, `POST /api/users/{id}/deactivate`                                               | Another role, or deactivation. Both end the user's sessions                                                                                                         | `users.manage` |
| `POST /api/users/{id}/activate`                                                                             | Brings a deactivated user back, with their role and password, and no session                                                                                        | `users.manage` |
| `POST /api/users/{id}/reset-password`                                                                       | A new initial password from `initialPassword`, which the user has to change at their next login. Unlocks the account and ends the user's sessions. Not for your own | `users.manage` |

- These handlers check twice: `[HasPermission]` reads the token, and `CurrentPermissions` reads the database. So someone deactivated, demoted or whose password was reset a moment ago can't use the token they still hold here. Any other endpoint with only `[HasPermission]` stays open to them until the token expires.
- Nobody gives more than they have, or reaches above themselves: a role can only be edited or deleted by someone who holds all its permissions, old and new, and a user can only get a role whose permissions the caller holds. Nobody changes a user who has permissions they don't.
- The Admin role can't be renamed, restricted or deleted, and the last active user with it can't be deactivated or moved. `LastAdminGuard` holds that when requests race: `roles.holders_version` changes when someone leaves the role, and `users.role_id` and `users.is_active` are concurrency tokens, so a save based on an old read gets a 409 `User.ChangedMeanwhile` and changes nothing.
- Names and emails refuse control and invisible characters. Role names are unique whatever their case, which `ux_roles_lower_name` holds in the database.
- What MVC refuses before a handler runs, such as a missing field or a value of the wrong type, answers 400 with code `Validation` and the field in camelCase, like the validators. Tests stop a request at a chosen read of the clock with `ManagementHandlerTest.PausedAtClockCall`, to put another request exactly before its save.
- Swagger, on http://localhost:5200/swagger in Development only, takes the `accessToken` from `POST /api/auth/login` in its Authorize button.

## Projects

| Endpoint                                                                    | Does                                                                                                                                                                 | Needs                                   |
| --------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| `GET /api/projects?page=1&pageSize=50&includeArchived=false`                | A `Page` of the projects the caller can open, by name, each with its owners, up to 4 members, the member count, open, overdue and finished tasks and its last change | signed in                               |
| `POST /api/projects`                                                        | A project with `name`, `key` and `description`. Its creator is its owner, with the default columns                                                                   | `projects.create`                       |
| `GET /api/projects/{id}`                                                    | The project, archived or not, with `myRole`, `canManage` and its columns                                                                                             | member, `projects.view_all` or `manage` |
| `PUT /api/projects/{id}`                                                    | Another name and description. The key never changes                                                                                                                  | owner or `projects.manage`              |
| `DELETE /api/projects/{id}`, `POST /api/projects/{id}/restore`              | Archives the project, or brings it back. Nothing deletes a project                                                                                                   | owner or `projects.manage`              |
| `GET /api/projects/{id}/members`, `GET /api/projects/{id}/members/{userId}` | The members, owners first, deactivated ones too                                                                                                                      | member, `projects.view_all` or `manage` |
| `POST /api/projects/{id}/members`, `PUT` and `DELETE …/members/{userId}`    | Adds an active user as `Owner` or `Member`, changes the role, removes them                                                                                           | owner or `projects.manage`              |
| `GET /api/projects/{id}/member-candidates?search=`                          | Up to 20 active users who aren't in the project, by name                                                                                                             | owner or `projects.manage`              |

- A key is 2 to 5 letters from A to Z, stored in capitals and unique among archived projects too (`ux_projects_key`), since task keys like `KAN-123` stay valid. `next_task_number` starts at 1.
- An archived project still opens, but only restoring changes it: anything else answers 409 `Project.Archived`.
- A project always keeps an active owner: removing or demoting an owner when no other active owner stays answers 409 `Project.LastOwner`, also for `projects.manage`. Deactivating a user isn't refused for their projects, so the handlers read which members are active and give those ids to `Project.RemoveMember` and `ChangeMemberRole`. A project left with only deactivated owners waits for `projects.manage` to make a new owner, after which the old ones can go. A change to the members also saves the project, so its `xmin` is checked: two owners taking each other away at once get one 409 `Project.ChangedMeanwhile` (`AuditingInterceptor`).
- The list holds the caller's projects, or every one with `projects.view_all` or `projects.manage`, read from the database as `IProjectAccess` does, and nothing for a deactivated user. Archived projects come only with `includeArchived=true`. Open tasks are outside a done column, overdue ones open past their deadline, and neither counts archived tasks.
- Roles go over the wire as text, `"Owner"` or `"Member"`.
- `mine=true` keeps only the projects the caller is a member of, which `projects.view_all` and `projects.manage` otherwise widen to every one: the main panel's "Proiectele mele". Every row has `completedTaskCount`, its tasks not archived with `completed_at`, so the panel's progress is finished / (open + finished).

## Board columns

| Endpoint                                                                 | Does                                                                                                                               | Needs                                   |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| `GET /api/projects/{id}/board-columns`, `GET …/board-columns/{columnId}` | The columns, from the left                                                                                                         | member, `projects.view_all` or `manage` |
| `POST /api/projects/{id}/board-columns`                                  | A column with `name` and `isDone`, at the right end                                                                                | owner or `projects.manage`              |
| `PUT /api/projects/{id}/board-columns/{columnId}`                        | Another `name` and `isDone`. Turning a column done completes its open tasks now, and turning it back opens them                    | owner or `projects.manage`              |
| `PATCH /api/projects/{id}/board-columns/{columnId}/position`             | Moves it to `position`, from 0, past the end meaning last, and answers every column in order                                       | owner or `projects.manage`              |
| `DELETE /api/projects/{id}/board-columns/{columnId}`                     | Deletes an empty column. One that holds a task, archived ones too, answers 409 `Column.NotEmpty`, the last one `Column.LastColumn` | owner or `projects.manage`              |

- The columns belong to the `Project` aggregate (`AddColumn`, `RenameColumn`, `MarkColumnDone`, `MoveColumn`, `RemoveColumn`), so their places stay 0, 1, 2... A change to them also saves the project (`AuditingInterceptor`), whose `xmin` makes the second of two changes at once a 409 `Project.ChangedMeanwhile`.
- A task moved or created into a column that was deleted meanwhile answers 400 on `columnId`, and a delete that comes after a task moved in answers `Column.NotEmpty`: the foreign key from `task_items.column_id` decides.
- Names are 1 to 100 characters, without hidden ones. An archived project answers 409 `Project.Archived`.

## Tasks

| Endpoint                                                                                | Does                                                                                                                                                                                                                                                                     | Needs                                   |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------- |
| `POST /api/projects/{id}/tasks`                                                         | A task with `title`, `description`, `priority`, and optionally `columnId`, `assigneeIds` and `dueDate`                                                                                                                                                                   | `tasks.create` and a real membership    |
| `GET /api/tasks/{id}`, `GET /api/tasks/by-key/{key}`                                    | The task, by id or by its key in any case (`kan-12`), with `canChange`; archived ones with `includeArchived`                                                                                                                                                             | member, `projects.view_all` or `manage` |
| `GET /api/projects/{id}/board`                                                          | The columns from the left and every open task, without pages, each with its place, assignees and counts, and `canMove` (a real member of an open project) and `canManage` (an owner or `projects.manage` of an open one)                                                 | member, `projects.view_all` or `manage` |
| `GET /api/projects/{id}/tasks`                                                          | A `Page` of the project's tasks, by key from the newest. `sort` takes key, title, column, priority, dueDate, assignee or updated, then `:asc` or `:desc`; filters `assigneeId` (one of the assignees), `priority` (repeated), `overdue`, `search`, and `includeArchived` | member, `projects.view_all` or `manage` |
| `PUT /api/tasks/{id}/title`, `…/description`, `…/priority`, `…/due-date`, `…/assignees` | Changes one property, each with its history entry, and returns the task. `assignees` takes `assigneeIds`, the whole list, which replaces the one before                                                                                                                  | a real membership                       |
| `PATCH /api/tasks/{id}/position`                                                        | Moves the task to `position` in `columnId`, from 0; without a position, or past the end, it goes last                                                                                                                                                                    | a real membership                       |
| `DELETE /api/tasks/{id}`                                                                | Archives the task: the row, its history and the rest stay                                                                                                                                                                                                                | a real membership                       |
| `POST /api/tasks/{id}/restore`                                                          | Brings an archived task back                                                                                                                                                                                                                                             | `tasks.archive`, and seeing the project |
| `GET /api/tasks/{id}/history?page=1&pageSize=50`                                        | A `Page` of the task's changes, from the newest, each with `field`, `oldValue`, `newValue`, `changedAt`, `changedBy` and the `names` of the ids in its values; a `Description` entry with both values null                                                               | member, `projects.view_all` or `manage` |
| `GET /api/tasks/{id}/history/{entryId}`                                                 | One entry of that history, whole, a description's texts included; another task's is 404 `History.NotFound`                                                                                                                                                               | member, `projects.view_all` or `manage` |

- A task's key is `{Project.Key}-{Number}`. The number comes from `ITaskNumbers`: one `UPDATE projects SET next_task_number = next_task_number + 1 … RETURNING` in the transaction that saves the task, with `ux_task_items_project_id_number` behind it. The UPDATE holds the project's row until the commit, so creations in one project go one after another, each with its own number and the end of its column as it is by then, with no retry and no 409. It skips an archived project, which answers 409 `Project.Archived`. Numbers aren't reused, archived tasks keep theirs.
- The counter doesn't go through the project's `xmin`, so a change to the members never stops a creation. The other way round, a change to the members that read the project before a creation took its number gets 409 `Project.ChangedMeanwhile` (SPEC section 5.3).
- A save inside a transaction holds its domain events back until the first save after it (`DomainEventsInterceptor`), which `TaskNumbers` makes right after its commit, so nobody hears of a task before it's committed.
- A real membership is a row in `project_members` with an active account (`IProjectAccess.EnsureRealMemberAsync`): `projects.view_all` and `projects.manage` see a project's tasks, but add one only once they're members (SPEC section 4.4).
- Archived tasks come only with `includeArchived=true`, which answers 403 `Auth.PermissionRequired` without `tasks.archive`. Queries that join projects archived or not use `db.Tasks(includeArchived)`: `IgnoreQueryFilters` anywhere in a query turns the task filter off too.
- A task has any number of assignees, in `task_assignees` (`task_id`, `user_id`, `assigned_at`, `assigned_by_id`, which is null only for the ones the `AddTaskAssignees` migration moved from the old `assignee_id`). They are part of `TaskItem`, read with it (`AutoInclude`), and every DTO lists them by name, whatever the case.
- Only active members of the project can be assigned, each once, or the whole change answers 400 on `assigneeIds`. Someone deactivated after being assigned stays on the task until someone takes them off. A column has to be on the project's board (400 on `columnId`). A task without a column starts in the first one, and goes at the end of its column.
- `assigneeId` in the list finds every task that has them among its assignees, and the sort by assignee goes by each task's first name in alphabetical order, whatever the case, with the unassigned ones last.
- A change of assignees writes one `Assignees` history entry: the old ids and the new ones, sorted and separated by commas, empty for nobody. It changes no column of `task_items`, so it also saves the task (`AuditingInterceptor`), whose `xmin` makes the second of two changes at once a 409 `Task.ChangedMeanwhile`; two adding the same person meet on the key of `task_assignees` first, which answers the same 409.
- Changes go through `TaskChanges`: an open task, a real member, an open project (409 `Project.Archived` otherwise), then the task's `xmin`, so the second of two changes at once gets 409 `Task.ChangedMeanwhile` and changes nothing. The entity writes the history, and a value that doesn't change writes none. Dates go in it in ISO, in UTC, to the microsecond, as PostgreSQL keeps them, so a restore writes the very moment the archiving wrote.
- A move renumbers the open tasks of the column it left and of the one it entered, 0, 1, 2..., ties by number, archived tasks taking no place. Every task whose place changes is saved with its `xmin`, so of two moves through one column at once the second gets 409 `Task.ChangedMeanwhile`. Only a change of column goes in the history (`ColumnId`, which the reports read); the others move through `ShiftTo`, quietly.
- The history (`GetTaskHistoryQuery`) is for whoever sees the project, and an archived task's only for `tasks.archive`, from the token, as its comments: anyone else gets 404 `Task.NotFound` (`TaskVisibility`). It goes from the newest, entries of one moment by their id, so none is on two pages. `changedBy` names whoever made the change, deactivated or not. `names` gives each id in the values of `Assignees` and `ColumnId` its name: a user's, also for someone who left the project, and a column's, `null` for one deleted since. The other fields have empty `names`. In the list a `Description` entry comes with `oldValue` and `newValue` null, which the query doesn't even read (`TaskHistoryRows.Read`), so a page doesn't carry 50 texts of up to 40 kB; `GetTaskHistoryEntryQuery` gives one entry whole, under the list's rules and with its names, and an entry that isn't the task's answers 404 `History.NotFound`. The panel asks for it only when someone opens the diff, which the browser computes. Nothing about the history goes to the hub: the panel reads it again after its own saves.
- Overdue means open past the deadline, outside a done column, as in the project list. The search finds a part of the title in any case, and `%` or `_` are only characters there.
- Priorities go over the wire as text, `"Low"`, `"Medium"`, `"High"` or `"Critical"`.
- `TaskAssignedEvent` carries the ids added and removed, and gives each assignee newly added a `TaskAssigned` notification, the same message for each, saved at once: not those already on the task, nor whoever made the change, also when a task is created for people (`TaskAssignedEventHandler`). Domain event handlers run after the commit, in the same scope, so one that saves calls `SaveChangesAsync` again; a handler that throws is logged and skipped. Every `Notification` raises `NotificationCreatedEvent`, which SignalR sends to its user, and its message is English, cut at 500 characters.

## Notifications

| Endpoint                                                                 | Does                                                                                                                                                                                                                                                                     | Needs     |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------- |
| `GET /api/notifications?page=1&pageSize=20&unread=true&type=TaskOverdue` | A `Page` of the caller's own, from the newest, 20 by default and 100 at most, only the unread with `unread=true`, only the types given with `type`, repeated; each with `id`, `type`, `message`, `createdAt`, `readAt` and `task` (`id`, `key`, `title` of now, or null) | signed in |
| `GET /api/notifications/unread-count`                                    | `{ count }` of the caller's unread                                                                                                                                                                                                                                       | signed in |
| `POST /api/notifications/{id}/read`                                      | The notification, read; a second time keeps the first `readAt`. Another's, or none, is 404 `Notification.NotFound`                                                                                                                                                       | signed in |
| `POST /api/notifications/read-all`                                       | Reads every unread one of the caller's in one `UPDATE`, and answers `{ count }`, how many it marked                                                                                                                                                                      | signed in |

- No permission, and nobody reaches another's: each handler reads the caller's own. A notification stays in the list when its user can no longer open its task, which then answers 403 or 404 as it would anyway; its task comes archived or not, in a project archived or not (`NotificationQueries.AsDtos`). Notifications of one moment come by their id, from the last, so none is on two pages.
- A `type` the API doesn't know, a `page` or `pageSize` out of 1 to 100, or an `unread` that isn't `true` or `false` answer 400 on that field. Reading isn't a change of the task: no history, no `updated_at`.
- A `Notification` is in-app only: its user, its type (`TaskOverdue`, `TaskAssigned` or `CommentAdded`), its task, an English message cut at 500 characters, and when it was read. Every one raises `NotificationCreatedEvent`, which SignalR sends to its user's group (below).
- `OverdueTasksWorker` (Infrastructure, a `BackgroundService` with a `PeriodicTimer`) looks for overdue tasks at start, after `DatabaseInitializer` has migrated the database, then every `Alerts:OverdueCheckInterval`. Each run sends `NotifyOverdueTasksCommand` (Application) in a scope of its own, without a user; a run that throws or fails is logged as an error, and the next one still comes. `Alerts:OverdueCheckEnabled` false stops it, which `ApiFactory` does: the API's tests run the command themselves.
- The command takes every task past its deadline (`due_date < now`), not completed, not archived, in a project that isn't archived, and tells its assignees and the project's owners who are active members, each once: a `TaskOverdue` notification, "KAN-1: Login SSO is overdue", all in one save. Nobody is told twice of the same task, ever: a pair of task and person that already has its `TaskOverdue` gets nothing, so someone assigned later to a task already overdue is told at the next run, and a deadline moved away that passes again tells nobody. A first run after a deploy tells everyone of the tasks already overdue, once. It logs how many notifications it made, for how many tasks.

## The main panel and my tasks

| Endpoint                                             | Does                                                                                                                                                                                                                                                                                                                                                    | Needs     |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| `GET /api/me/summary?weekStart=2026-11-02T00:00:00Z` | `openTasks`, `overdueTasks` (open ones past their deadline), `completedThisWeek` (completed since `weekStart`) and `activeProjects` (open projects the caller is a member of)                                                                                                                                                                           | signed in |
| `GET /api/me/tasks?page=1&pageSize=50&open=true`     | A `Page` of the caller's own tasks, by deadline from the soonest, those without one last, then by key from the newest; the project list's `sort`, `priority`, `overdue`, `search` and `includeArchived`, but no `assigneeId`, and `open=true` for those not completed. Each row is the project list's, with `projectId`, `projectKey` and `projectName` | signed in |

- The caller's own tasks (`TaskQueries.MyTasks`) are those they are assigned to, not archived, in projects not archived where they are still a member, and none for a deactivated account: `projects.view_all` and `projects.manage` widen nothing here, and a task of a project they left, where they stay assigned, doesn't count. Both handlers read them, and the summary counts in one query.
- `weekStart` is the Monday at midnight in the caller's own time, which the browser sends as an instant with its offset; it is required and at most now, or the summary answers 400 on it. The handler turns it into UTC first, since Npgsql writes only UTC to a `timestamptz`.
- Open means `completed_at` null. `overdue` in the list stays the project list's: open past the deadline, outside a done column.
- The project's list and this one share `TaskListRows`: the filters, the rows and the sorts. The key is the project's key, then the number. Ties go by key in the same direction in the project's list, and from the newest whichever the direction here, as the default sort's do.

## Comments

| Endpoint                                           | Does                                                                      | Needs                                                        |
| -------------------------------------------------- | ------------------------------------------------------------------------- | ------------------------------------------------------------ |
| `GET /api/tasks/{id}/comments`                     | Every comment of the task, from the oldest, without pages, with `canEdit` | member, `projects.view_all` or `manage`                      |
| `POST /api/tasks/{id}/comments`                    | A comment with `body`, answered with where it is, `/api/comments/{id}`    | a real membership, an open project and an open task          |
| `GET /api/comments/{id}`, `PUT /api/comments/{id}` | One comment, and another `body` for it                                    | reading as for the list; changing, its author, as for adding |

- A body is basic markdown, like a description, of 1 to 5,000 characters, lines and tabs kept, other hidden characters refused (400 on `body`), its blank end trimmed. Nothing deletes a comment in v1: `DELETE` answers 405.
- Someone else's comment answers 403 `Comment.NotYours`, even for the project's owner; a missing one 404 `Comment.NotFound`. An archived task answers 409 `Task.Archived` to a new comment or an edit, and its comments are there only for `tasks.archive`, from the token, as the task is: anyone else gets 404 `Task.NotFound` (`TaskVisibility`).
- A comment comes through `TaskItem.AddComment`, which raises `CommentAddedEvent`. It isn't a change of the task: no history, no `updated_at`. `CommentAddedEventHandler` gives the task's assignees and its creator a `CommentAdded` notification, each once, not the author, "Ana commented on KAN-1: Login SSO", all in one save, and only to those who can still open the task: active members of the project. An edit notifies nobody.

## Attachments

| Endpoint                           | Does                                                                                                                | Needs                                                                   |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `POST /api/tasks/{id}/attachments` | One file, as multipart in the field `file`, answered with where it is, `/api/attachments/{id}`. The task lists them | a real membership, an open project and an open task                     |
| `GET /api/attachments/{id}`        | The file, always as a download (`Content-Disposition: attachment`, `nosniff`), under the name it came with          | member, `projects.view_all` or `manage`                                 |
| `DELETE /api/attachments/{id}`     | Deletes the row, then the file on disk                                                                              | whoever uploaded it, still a real member, or `tasks.archive`; open task |

- A file goes up to 25 MB, 25 × 1,048,576 bytes, with an extension of `Storage:AllowedExtensions`, the last one after a dot, in any case. That setting is one value separated by commas, `pdf,png,jpg,jpeg,gif,txt,md,csv,xlsx,docx,zip` in `appsettings.json`, so `Storage__AllowedExtensions=pdf,png` replaces the whole list; the API refuses to start without an extension, or with a list by index (`Storage__AllowedExtensions__0`), which binding to one value would drop without a word. Anything else answers 400 on `file`: a bigger file, another extension, a name empty, over 255 characters or with a hidden character, no file, two files or another field. The request may be 1 MB bigger than the file; above that MVC doesn't read the form (`DisableFormValueModelBinding`) and the action answers the same 400. nginx stops a request over 30 MB with 413.
- The name keeps only what comes after the last `/` or `\`, and never reaches the disk: `LocalFileStorage` keeps the file as `{yyyy}/{MM}/{guid}{ext}` under `Storage:Root`, the extension in lower case, and refuses a key leading outside the root. The content type comes from the extension. A relative root is taken from the folder the API runs in.
- The file goes to disk first, then its row; a save that fails deletes the file again. Deleting goes the other way, and a file the disk keeps after its row went is a warning in the log. The task lists its files in its DTO, from the first, with `canDelete`.
- An archived task answers 409 `Task.Archived` to an upload and a delete, and its files are there only for `tasks.archive`, as its comments: anyone else gets 404 `Attachment.NotFound`. Someone else's file answers 403 `Attachment.NotYours`.
- `TaskItem.AddAttachment` and `RemoveAttachment` raise `AttachmentsChangedEvent`, which the board hears as `TaskUpdated` with `Attachments`. Attachments aren't in the history.

## Real time

| Hub           | Does                                                                                                                                                     | Needs                                         |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `/hubs/board` | Every connection joins `user:{id}` by itself. `JoinProject(projectId)` joins `project:{id}`, and `LeaveProject(projectId)` leaves it. The hub only sends | signed in; joining as for the project's `GET` |

- The token comes from `access_token` in the query string, since a browser can't put a header on a WebSocket, and only on `/hubs`: under `/api` it opens nothing (`JwtAuthenticationExtensions`). The name has to be in the address as sent, once: ASP.NET decodes `access%5Ftoken` into the same name, which the logs' redaction wouldn't see. The forced password change closes the hub too (403 `Auth.PasswordChangeRequired`).
- The hub speaks WebSockets only. A long poll, or a send of Server-Sent Events, may bring a fresh token and push the connection's expiry back, so a user who lost a project would keep hearing it for ever.
- `JoinProject` asks `IProjectAccess.EnsureMemberAsync`, so a member, `projects.view_all` or `projects.manage`, and an active account. It answers null for a join, and the code for a refusal, `Project.MemberRequired`, with a warning in the log: a refusal is an ordinary answer, and SignalR would log a method that throws as an `Error`. Joins and refusals are logged with `UserId`, `ProjectId` and `ConnectionId`.
- A connection closes when the token it was opened with expires (`CloseOnAuthenticationExpiration`), so someone who lost access stops hearing a project within 15 minutes; the browser opens another with a fresh token.
- Tokens never reach a log: `AccessTokenRedaction` hides the value of `access_token` in every log entry, such as ASP.NET's "Request starting", which Development shows with the whole address, and nginx keeps no access log for `/hubs/`.
- `IRequestContext` (Application, next to `ICurrentUser`) gives the connection id the browser sends in `X-Connection-Id`, read by `HttpRequestContext`: only an id SignalR could make, letters, digits, `-` and `_`, at most 64, sent once; otherwise null.
- What the hub sends comes from the domain event handlers in `Features/Realtime`, after the commit, through `IBoardNotifier` (`HubBoardNotifier` in Api, over `IHubContext<BoardHub>`):

| Event                                         | From                                                                                                                              | Payload                                                                                          | Group          |
| --------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | -------------- |
| `TaskCreated`, `TaskArchived`, `TaskRestored` | their task events                                                                                                                 | `taskId`, `projectId`, `byUserId`, `originConnectionId`                                          | `project:{id}` |
| `TaskMoved`                                   | `TaskMovedEvent`, the moved task only                                                                                             | the same, and `columnId` and `position`, where it went                                           | `project:{id}` |
| `TaskUpdated`                                 | `TaskUpdatedEvent`, `TaskAssignedEvent` with `Assignees`, a creation's included, and `AttachmentsChangedEvent` with `Attachments` | the same, and `field`: `Title`, `Description`, `Priority`, `DueDate`, `Assignees`, `Attachments` | `project:{id}` |
| `CommentAdded`                                | `CommentAddedEvent`                                                                                                               | the same, and `commentId`                                                                        | `project:{id}` |
| `NotificationCreated`                         | `NotificationCreatedEvent`                                                                                                        | `notificationId`                                                                                 | `user:{id}`    |

- `originConnectionId` is the request's `X-Connection-Id`, or null without one, so the tab that made a change can ignore it. A hub that fails is logged and skipped, like any handler: the change is saved, and a board reads itself again when it reconnects.
- Tests connect as the browser does through `RealtimeApiTest`: negotiation and WebSocket on the test server, with the token in the address. Handler tests read what was sent from the `BoardNotifier` substitute, and set the connection through `RequestContext`.

## Browser logs

`POST /api/client-logs` takes up to 10 warnings and errors from any signed-in browser, and writes each into the API's log at its level, with `ClientMessage`, `ClientUrl`, `RequestCorrelationId` (the request the entry is about), `ClientDetails` and `UserAgent`. The log context adds the user and the batch's own correlation id. A batch also says how many entries the browser dropped over its limit of 10 a minute, which the API logs as a warning. It answers 204, or 400 for a batch that's empty, too big, at another level or too long. It isn't open before a forced password change, nor without a session.

## Authentication

- `POST /api/auth/login`, `refresh`, `logout` and `change-password`, and `GET /api/auth/me`. Access tokens go in the body and refresh tokens only in the `refresh_token` cookie, which is `Secure`, so tests have to call over `https://localhost` and browsers accept it on `http://localhost`.
- Every controller needs a signed-in user unless it says `[AllowAnonymous]`.
- While `MustChangePassword` is on, the API answers 403 `Auth.PasswordChangeRequired` everywhere except the public endpoints and those marked `[AllowedBeforePasswordChange]`.
- Logins are limited to 5 per minute per client address, which comes from `X-Forwarded-For` on connections from private networks. Tests that log in give each attempt its own address: `AuthApiTest.Login` does it by itself.
