# Frontend

Angular 22 single page application.

## Connecting to the backend

The frontend calls `/api` and `/hubs` on its own origin, in development and in production, so there is no CORS anywhere.

- In development, `npm start` uses `proxy.conf.json` to send both to the API on http://localhost:5200, with WebSockets for `/hubs`. Start the API first: `backend/CLAUDE.md` says how.
- In Docker and production, `nginx.conf` and `nginx.prod.conf` do the same, sending them to the `api` service.

## Commands

Run from `frontend/`:

- Serve: `npm start` (serves on http://localhost:4200)
- Test: `npm test -- --watch=false`
- Build: `npm run build`
- Lint: `npm run lint`
- Check formatting: `npx prettier --check .`

## UI

`docs/UI_UX_SPEC.md` decides how everything looks and behaves. Read the section for what you're building.

- Components only use `var(--kb-*)`, never hex values. Material takes its colors from the theme in `src/styles.scss`.
- No `::ng-deep`. Material is styled through the `mat.*-overrides` mixins in `src/styles.scss`.
- Buttons: `matButton="filled"` with `kb-default` once per surface, or with `kb-danger` for destructive actions; `matButton="outlined"` with `kb-bezel`; plain `matButton` for text. No `elevated` or `tonal`.
- Fonts and icons are self-hosted, because the app runs on the LAN. Nothing comes from Google Fonts or a CDN.
- Text is in Romanian, in sentence case, and goes through `i18n`.

## Access rules

- Hiding something in the UI is not access control: the backend enforces permissions.
- Show or hide based on permissions, never role names.
