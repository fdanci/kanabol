# Backend

.NET Core with PostgreSQL. Not scaffolded yet: add the build, test and migration commands, required env vars and local database setup here once they exist.

## Access rules
- Code checks permissions, never role names.
- Visibility across all projects comes from the global `projects.view_all` permission, not from a role.
- Access is checked on every request. The frontend only hides what isn't available.
