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
- Run the task checks, from the repo root: `scripts/check-tasks.sh [task-number...]`, under the same conditions.

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
- The API refuses to start if `Jwt:Secret` (at least 32 characters), `Storage:Root` or the connection string is missing, or if a new database has no `Admin:InitialPassword` (at least 10 characters).

In Docker and production, settings come from environment variables:

| Variable                               | Value                                                      |
| -------------------------------------- | ---------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`               | `Production` unless set                                    |
| `ConnectionStrings__Default`           | The PostgreSQL connection string                           |
| `Jwt__Secret`                          | The key that signs JWTs                                    |
| `Admin__InitialPassword`               | The first admin's password, needed on the first start only |
| `Storage__Root`                        | The attachments folder, `/data/attachments` by default     |
| `Database__MigrateOnStartup`           | `true` by default                                          |
| `Serilog__WriteTo__1__Args__serverUrl` | Seq's address, `http://seq:5341` by default                |

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

| Endpoint                                                                    | Does                                                                                                                                                       | Needs                                   |
| --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| `GET /api/projects?page=1&pageSize=50&includeArchived=false`                | A `Page` of the projects the caller can open, by name, each with its owners, up to 4 members, the member count, open and overdue tasks and its last change | signed in                               |
| `POST /api/projects`                                                        | A project with `name`, `key` and `description`. Its creator is its owner, with the default columns                                                         | `projects.create`                       |
| `GET /api/projects/{id}`                                                    | The project, archived or not, with `myRole`, `canManage` and its columns                                                                                   | member, `projects.view_all` or `manage` |
| `PUT /api/projects/{id}`                                                    | Another name and description. The key never changes                                                                                                        | owner or `projects.manage`              |
| `DELETE /api/projects/{id}`, `POST /api/projects/{id}/restore`              | Archives the project, or brings it back. Nothing deletes a project                                                                                         | owner or `projects.manage`              |
| `GET /api/projects/{id}/members`, `GET /api/projects/{id}/members/{userId}` | The members, owners first, deactivated ones too                                                                                                            | member, `projects.view_all` or `manage` |
| `POST /api/projects/{id}/members`, `PUT` and `DELETE …/members/{userId}`    | Adds an active user as `Owner` or `Member`, changes the role, removes them                                                                                 | owner or `projects.manage`              |
| `GET /api/projects/{id}/member-candidates?search=`                          | Up to 20 active users who aren't in the project, by name                                                                                                   | owner or `projects.manage`              |

- A key is 2 to 5 letters from A to Z, stored in capitals and unique among archived projects too (`ux_projects_key`), since task keys like `KAN-123` stay valid. `next_task_number` starts at 1.
- An archived project still opens, but only restoring changes it: anything else answers 409 `Project.Archived`.
- A project always keeps an active owner: removing or demoting an owner when no other active owner stays answers 409 `Project.LastOwner`, also for `projects.manage`. Deactivating a user isn't refused for their projects, so the handlers read which members are active and give those ids to `Project.RemoveMember` and `ChangeMemberRole`. A project left with only deactivated owners waits for `projects.manage` to make a new owner, after which the old ones can go. A change to the members also saves the project, so its `xmin` is checked: two owners taking each other away at once get one 409 `Project.ChangedMeanwhile` (`AuditingInterceptor`).
- The list holds the caller's projects, or every one with `projects.view_all` or `projects.manage`, read from the database as `IProjectAccess` does, and nothing for a deactivated user. Archived projects come only with `includeArchived=true`. Open tasks are outside a done column, overdue ones open past their deadline, and neither counts archived tasks.
- Roles go over the wire as text, `"Owner"` or `"Member"`.

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

| Endpoint                                                                               | Does                                                                                                                                                                                                                                              | Needs                                   |
| -------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| `POST /api/projects/{id}/tasks`                                                        | A task with `title`, `description`, `priority`, and optionally `columnId`, `assigneeId` and `dueDate`                                                                                                                                             | `tasks.create` and a real membership    |
| `GET /api/tasks/{id}`, `GET /api/tasks/by-key/{key}`                                   | The task, by id or by its key in any case (`kan-12`), with `canChange`; archived ones with `includeArchived`                                                                                                                                      | member, `projects.view_all` or `manage` |
| `GET /api/projects/{id}/tasks`                                                         | A `Page` of the project's tasks, by key from the newest. `sort` takes key, title, column, priority, dueDate, assignee or updated, then `:asc` or `:desc`; filters `assigneeId`, `priority` (repeated), `overdue`, `search`, and `includeArchived` | member, `projects.view_all` or `manage` |
| `PUT /api/tasks/{id}/title`, `…/description`, `…/priority`, `…/due-date`, `…/assignee` | Changes one property, each with its history entry, and returns the task                                                                                                                                                                           | a real membership                       |
| `PATCH /api/tasks/{id}/position`                                                       | Moves the task to `position` in `columnId`, from 0; without a position, or past the end, it goes last                                                                                                                                             | a real membership                       |
| `DELETE /api/tasks/{id}`                                                               | Archives the task: the row, its history and the rest stay                                                                                                                                                                                         | a real membership                       |
| `POST /api/tasks/{id}/restore`                                                         | Brings an archived task back                                                                                                                                                                                                                      | `tasks.archive`, and seeing the project |

- A task's key is `{Project.Key}-{Number}`. The number comes from `ITaskNumbers`: one `UPDATE projects SET next_task_number = next_task_number + 1 … RETURNING` in the transaction that saves the task, with `ux_task_items_project_id_number` behind it. The UPDATE holds the project's row until the commit, so creations in one project go one after another, each with its own number and the end of its column as it is by then, with no retry and no 409. It skips an archived project, which answers 409 `Project.Archived`. Numbers aren't reused, archived tasks keep theirs.
- The counter doesn't go through the project's `xmin`, so a change to the members never stops a creation. The other way round, a change to the members that read the project before a creation took its number gets 409 `Project.ChangedMeanwhile` (SPEC section 5.3).
- A save inside a transaction holds its domain events back until the first save after it (`DomainEventsInterceptor`), which `TaskNumbers` makes right after its commit, so nobody hears of a task before it's committed.
- A real membership is a row in `project_members` with an active account (`IProjectAccess.EnsureRealMemberAsync`): `projects.view_all` and `projects.manage` see a project's tasks, but add one only once they're members (SPEC section 4.4).
- Archived tasks come only with `includeArchived=true`, which answers 403 `Auth.PermissionRequired` without `tasks.archive`. Queries that join projects archived or not use `db.Tasks(includeArchived)`: `IgnoreQueryFilters` anywhere in a query turns the task filter off too.
- Only an active member of the project can be assigned (400 on `assigneeId`), and a column has to be on the project's board (400 on `columnId`). A task without a column starts in the first one, and goes at the end of its column.
- Changes go through `TaskChanges`: an open task, a real member, an open project (409 `Project.Archived` otherwise), then the task's `xmin`, so the second of two changes at once gets 409 `Task.ChangedMeanwhile` and changes nothing. The entity writes the history, and a value that doesn't change writes none.
- A move renumbers the open tasks of the column it left and of the one it entered, 0, 1, 2..., ties by number, archived tasks taking no place. Every task whose place changes is saved with its `xmin`, so of two moves through one column at once the second gets 409 `Task.ChangedMeanwhile`. Only a change of column goes in the history (`ColumnId`, which the reports read); the others move through `ShiftTo`, quietly.
- Overdue means open past the deadline, outside a done column, as in the project list. The search finds a part of the title in any case, and `%` or `_` are only characters there.
- Priorities go over the wire as text, `"Low"`, `"Medium"`, `"High"` or `"Critical"`.
- `TaskAssignedEvent` gives the new assignee a `TaskAssigned` notification, unless they assigned the task to themselves, also when a task is created for someone (`TaskAssignedEventHandler`). Domain event handlers run after the commit, in the same scope, so one that saves calls `SaveChangesAsync` again; a handler that throws is logged and skipped. Every `Notification` raises `NotificationCreatedEvent`, for SignalR in group 14, and its message is English, cut at 500 characters.

## Browser logs

`POST /api/client-logs` takes up to 10 warnings and errors from any signed-in browser, and writes each into the API's log at its level, with `ClientMessage`, `ClientUrl`, `RequestCorrelationId` (the request the entry is about), `ClientDetails` and `UserAgent`. The log context adds the user and the batch's own correlation id. A batch also says how many entries the browser dropped over its limit of 10 a minute, which the API logs as a warning. It answers 204, or 400 for a batch that's empty, too big, at another level or too long. It isn't open before a forced password change, nor without a session.

## Authentication

- `POST /api/auth/login`, `refresh`, `logout` and `change-password`, and `GET /api/auth/me`. Access tokens go in the body and refresh tokens only in the `refresh_token` cookie, which is `Secure`, so tests have to call over `https://localhost` and browsers accept it on `http://localhost`.
- Every controller needs a signed-in user unless it says `[AllowAnonymous]`.
- While `MustChangePassword` is on, the API answers 403 `Auth.PasswordChangeRequired` everywhere except the public endpoints and those marked `[AllowedBeforePasswordChange]`.
- Logins are limited to 5 per minute per client address, which comes from `X-Forwarded-For` on connections from private networks. Tests that log in give each attempt its own address: `AuthApiTest.Login` does it by itself.
