# Frontend

Angular 22 single page application. Not connected to the backend yet, and no linter is set up: add how it connects locally and the lint command here once they exist.

## Commands
Run from `frontend/`:

- Serve: `npm start` (serves on http://localhost:4200)
- Test: `npm test -- --watch=false`
- Build: `npm run build`

## Access rules
- Hiding something in the UI is not access control: the backend enforces permissions.
- Show or hide based on permissions, never role names.
