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

| Endpoint                                                                                                    | Does                                                        | Needs          |
| ----------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- | -------------- |
| `GET /api/roles`, `GET /api/roles/{id}`, `POST /api/roles`, `PUT /api/roles/{id}`, `DELETE /api/roles/{id}` | Roles and their permissions                                 | `roles.manage` |
| `GET /api/permissions`                                                                                      | Every permission that exists                                | `roles.manage` |
| `GET /api/users`, `GET /api/users/{id}`, `POST /api/users`, `PUT /api/users/{id}`                           | Users, with an initial password on creation                 | `users.manage` |
| `GET /api/users/assignable-roles`                                                                           | The roles this caller can give                              | `users.manage` |
| `PUT /api/users/{id}/role`, `POST /api/users/{id}/deactivate`                                               | Another role, or deactivation. Both end the user's sessions | `users.manage` |

- These handlers check twice: `[HasPermission]` reads the token, and `CurrentPermissions` reads the database. So someone deactivated or demoted a moment ago can't use the token they still hold here. Any other endpoint with only `[HasPermission]` stays open to them until the token expires.
- Nobody gives more than they have, or reaches above themselves: a role can only be edited or deleted by someone who holds all its permissions, old and new, and a user can only get a role whose permissions the caller holds. Nobody changes a user who has permissions they don't.
- The Admin role can't be renamed, restricted or deleted, and the last active user with it can't be deactivated or moved. `LastAdminGuard` holds that when requests race: `roles.holders_version` changes when someone leaves the role, and `users.role_id` is a concurrency token, so a save based on an old read gets a 409 `User.ChangedMeanwhile` and changes nothing.
- Names and emails refuse control and invisible characters. Role names are unique whatever their case, which `ux_roles_lower_name` holds in the database.
- What MVC refuses before a handler runs, such as a missing field or a value of the wrong type, answers 400 with code `Validation` and the field in camelCase, like the validators. Tests stop a request at a chosen read of the clock with `ManagementHandlerTest.PausedAtClockCall`, to put another request exactly before its save.
- Swagger, on http://localhost:5200/swagger in Development only, takes the `accessToken` from `POST /api/auth/login` in its Authorize button.

## Authentication

- `POST /api/auth/login`, `refresh`, `logout` and `change-password`, and `GET /api/auth/me`. Access tokens go in the body and refresh tokens only in the `refresh_token` cookie, which is `Secure`, so tests have to call over `https://localhost` and browsers accept it on `http://localhost`.
- Every controller needs a signed-in user unless it says `[AllowAnonymous]`.
- While `MustChangePassword` is on, the API answers 403 `Auth.PasswordChangeRequired` everywhere except the public endpoints and those marked `[AllowedBeforePasswordChange]`.
- Logins are limited to 5 per minute per client address, which comes from `X-Forwarded-For` on connections from private networks. Tests that log in give each attempt its own address: `AuthApiTest.Login` does it by itself.
