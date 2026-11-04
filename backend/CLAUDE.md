# Backend

.NET 10 minimal API with PostgreSQL. No database or tests yet: add the test and migration commands, required env vars and local database setup here once they exist.

## Commands
Run from `backend/`:

- Build: `dotnet build Kanabol.sln`
- Run: `dotnet run --project Kanabol.Api --launch-profile http` (serves on http://localhost:5200)
- Lint: `dotnet build Kanabol.sln -warnaserror` (analyzers; add `--no-incremental` if nothing changed since the last build)
- Check formatting: `dotnet format Kanabol.sln --verify-no-changes`

## Access rules
- Code checks permissions, never role names.
- Visibility across all projects comes from the global `projects.view_all` permission, not from a role.
- Access is checked on every request. The frontend only hides what isn't available.
