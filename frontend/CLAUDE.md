# Frontend

Angular 22 single page application.

## Connecting to the backend

The frontend calls `/api` and `/hubs` on its own origin, in development and in production, so there is no CORS anywhere. `src/environments/environment.ts` holds only those two paths, and the same build runs everywhere.

- In development, `npm start` uses `proxy.conf.json` to send both to the API on http://localhost:5200, with WebSockets for `/hubs`. Start the API first: `backend/CLAUDE.md` says how.
- In Docker and production, `nginx.conf` and `nginx.prod.conf` do the same, sending them to the `api` service.
- Feature code calls the API through `ApiClient` (`core/api`), in a `<feature>-api.ts`, never through `HttpClient` directly.

## Commands

Run from `frontend/`:

- Serve: `npm start` (serves on http://localhost:4200)
- Test: `npm test -- --watch=false`
- Build: `npm run build`
- Lint: `npm run lint`
- Check formatting: `npx prettier --check .`
- Extract the texts for translation: `npx ng extract-i18n --output-path <folder>`. Romanian is the source language, and v1 has no other.
- Run the acceptance checks of the skeleton and authentication, from the repo root: `scripts/check-web.sh [task-number...]`.

## How a request goes

- `correlationInterceptor` gives each API request an `X-Correlation-Id`. `errorInterceptor` shows a failed request in the error snackbar, with the end of that id, and passes the error on. It leaves a 401 to the session and a 400 with field errors to the form. A request that shows its own errors sets the `QUIET` context token.
- `authInterceptor` sends the access token. On a 401 it waits for one refresh, shared with whatever failed at the same time, and sends the request once more. If the session is over, the user goes to the login page.
- `AuthStore` keeps the access token in memory only, never in storage. At startup it gets a new one from the refresh cookie, before the first page. Tabs take turns to refresh, through `navigator.locks`: two tabs sending the same cookie at once would look like a stolen token to the API and end the session.
- `authGuard` opens a page to someone signed in, and only the password change page while `mustChangePassword` is on. `permissionGuard` is a `canMatch`: without the permission, the route after it on the same path matches and shows the 403 page at the address asked for. `withPermission` in `app.routes.ts` adds both.
- `Logger` is the only code that writes to the console. `GlobalErrorHandler` logs what nobody caught.

## UI

`docs/UI_UX_SPEC.md` decides how everything looks and behaves. Read the section for what you're building.

- Components only use `var(--kb-*)`, never hex values, in inline styles too. Material takes its colors from the theme in `src/styles.scss`.
- White text and checkmarks only go on `--kb-fill-accent` and `--kb-fill-danger`. `--kb-accent-600` and `--kb-danger-600` get lighter in dark, to read on the cards, and white text on them falls below AA there.
- No `::ng-deep`, and no rules on Material's own classes. Material is styled through the `mat.*-overrides` mixins in `src/styles.scss`, and our classes go on the host elements.
- Buttons: `matButton="filled"` with `kb-default` once per surface, or with `kb-danger` for destructive actions; `matButton="outlined"` with `kb-bezel`; plain `matButton` for text. No `elevated` or `tonal`. They're 32px; `kb-regular` brings one to 28px and `kb-sm` to 24px.
- Forms use `kb-field` on a plain `input` or `textarea`, in `kb-form-group` rows of `kb-form-row` with a `kb-form-label`, and `kb-field-error` under a field. `mat-form-field` is only for a one-field quick create.
- Menus take `class="kb-menu"`. Dialogs are sheets by default (`MAT_DIALOG_DEFAULT_OPTIONS` in `app.config.ts`), with a width from `SHEET_WIDTH`. `Confirmation.ask` asks before anything that can't be undone.
- Icons inside Material components are `<mat-icon>`, which draws with the self-hosted Material Symbols at the size its place gives it. Elsewhere a `span.material-symbols-outlined` with its own `font-size` is fine.
- Feedback on an action goes through `Snackbar` (`show`, and `error` with the correlation id). Lasting context goes in a `kb-banner`.
- Shared pieces live in `shared/ui`: `kb-banner`, `kb-chip`, `kb-empty-state`, `kb-skeleton`, `kb-upload-zone`, `kb-avatar`, `kb-page-header`, `kb-lockup`. Pages start with a `kb-page-header`.
- Fonts and icons are self-hosted, because the app runs on the LAN. Nothing comes from Google Fonts or a CDN.
- Text is in Romanian, in sentence case, and goes through `i18n`: the `i18n` attribute in templates, `$localize` in code. Dates and numbers format as `ro-RO`.

## Access rules

- Hiding something in the UI is not access control: the backend enforces permissions.
- Show or hide based on permissions, never role names: `*kbHasPermission="'users.manage'"`, with the names in `PERMISSIONS` (`core/auth/permissions.ts`).
