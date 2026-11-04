# Backend

.NET 10 minimal API with PostgreSQL. No database or tests yet: add the test and migration commands, required env vars and local database setup here once they exist.

## Commands
Run from `backend/`:

- Build: `dotnet build Kanabol.sln`
- Run: `dotnet run --project src/Kanabol.Api --launch-profile http` (serves on http://localhost:5200)
- Lint: `dotnet build Kanabol.sln -warnaserror` (analyzers; add `--no-incremental` if nothing changed since the last build)
- Check formatting: `dotnet format Kanabol.sln --verify-no-changes`

## Configuration
- Shared values are in `appsettings.json`, Development values in `appsettings.Development.json`.
- Development secrets go in user secrets, never in a file: `dotnet user-secrets set "Jwt:Secret" "<value>" --project src/Kanabol.Api`. The same applies to `ConnectionStrings:Default`.
- Local overrides that aren't secret go in `src/Kanabol.Api/appsettings.Development.local.json`, which git ignores. It's only read in Development, and it wins over everything else.
- In Docker and production, settings come from environment variables, with `__` as the separator: `Jwt__Secret`.
- The API refuses to start if `Jwt:Secret` (at least 32 characters), `Storage:Root` or the connection string is missing.

## Access rules
- Code checks permissions, never role names.
- Visibility across all projects comes from the global `projects.view_all` permission, not from a role.
- Access is checked on every request. The frontend only hides what isn't available.
