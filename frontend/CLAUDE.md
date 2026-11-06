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
- Run the acceptance checks of the skeleton and authentication, from the repo root: `scripts/check-web.sh [task-number...]`, those of the users and roles pages: `scripts/check-admin.sh [task-number...]`, those of the projects: `scripts/check-projects.sh [task-number...]`, and those of the tasks: `scripts/check-tasks.sh [task-number...]` (7 and 8 need only the packages). Task 3 of check-admin.sh and tasks 1 and 2 of check-projects.sh also start the real API, so they need Docker and PostgreSQL like `scripts/check-users.sh`.

## How a request goes

- `correlationInterceptor` gives each API request an `X-Correlation-Id`. `errorInterceptor` shows a failed request in the error snackbar, with the end of that id, and passes the error on. It leaves a 401 to the session and a 400 with field errors to the form. A request that shows its own errors sets the `QUIET` context token, and one that shows only some codes itself, such as an email already taken under its field, passes `handledHere('User.EmailTaken')` as its context. The refusals of `docs/SPEC.md` §4.4 have Romanian messages in `BY_CODE`.
- `authInterceptor` sends the access token. On a 401 it waits for one refresh, shared with whatever failed at the same time, and sends the request once more. If the session is over, the user goes to the login page.
- `AuthStore` keeps the access token in memory only, never in storage. At startup it gets a new one from the refresh cookie, before the first page. Tabs take turns to refresh, through `navigator.locks`: two tabs sending the same cookie at once would look like a stolen token to the API and end the session.
- `authGuard` opens a page to someone signed in, and only the password change page while `mustChangePassword` is on. `permissionGuard` is a `canMatch`: without the permission, the route after it on the same path matches and shows the 403 page at the address asked for. `withPermission` in `app.routes.ts` adds both.
- `Logger` is the only code that writes to the console. `GlobalErrorHandler` logs what nobody caught. In production the Logger also hands warnings and errors to `ClientLogs`, which sends them to `POST /api/client-logs` for Seq: gathered for 5 seconds, at most 10 a minute, quietly, and only while signed in, since a 401 there would end on the login page.

## UI

`docs/UI_UX_SPEC.md` decides how everything looks and behaves. Read the section for what you're building.

- Components only use `var(--kb-*)`, never hex values, in inline styles too. Material takes its colors from the theme in `src/styles.scss`.
- White text and checkmarks only go on `--kb-fill-accent` and `--kb-fill-danger`. `--kb-accent-600` and `--kb-danger-600` get lighter in dark, to read on the cards, and white text on them falls below AA there.
- No `::ng-deep`, and no rules on Material's own classes. Material is styled through the `mat.*-overrides` mixins in `src/styles.scss`, and our classes go on the host elements.
- Buttons: `matButton="filled"` with `kb-default` once per surface, or with `kb-danger` for destructive actions; `matButton="outlined"` with `kb-bezel`; plain `matButton` for text. No `elevated` or `tonal`. They're 32px; `kb-regular` brings one to 28px and `kb-sm` to 24px.
- Forms use `kb-field` on a plain `input` or `textarea`, in `kb-form-group` rows of `kb-form-row` with a `kb-form-label`, and `kb-field-error` under a field. `mat-form-field` is only for a one-field quick create.
- Menus take `class="kb-menu"`, and a destructive item `class="kb-menu-danger"`. Dialogs are sheets by default (`MAT_DIALOG_DEFAULT_OPTIONS` in `app.config.ts`), with a width from `SHEET_WIDTH`. `Confirmation.ask` asks before anything that can't be undone, and `Confirmation.discardChanges` before unsaved changes are lost. A sheet with a form calls `askBeforeClosing(ref, changed, confirmation)`, so Esc and a click beside it ask first.
- Tables are a `mat-table` with `kb-table`, inside a `kb-table-card`, with `kb-table-row` on each `mat-row` for the zebra and `kb-row-actions` on the ⋮ button, which shows on hover. A `mat-paginator` under it offers 25, 50 and 100, with the Romanian labels of `PaginatorLabels`.
- A select in a form is a `mat-select` with `class="kb-field kb-select"` and `panelClass="kb-menu"`, labelled by `aria-labelledby`.
- Lists of the API come in a `Page<T>` (`shared/models/page.ts`). The password policy is in `shared/models/password-policy.ts`.
- Icons inside Material components are `<mat-icon>`, which draws with the self-hosted Material Symbols at the size its place gives it. Elsewhere a `span.material-symbols-outlined` with its own `font-size` is fine.
- Feedback on an action goes through `Snackbar` (`show`, and `error` with the correlation id). Lasting context goes in a `kb-banner`.
- Shared pieces live in `shared/ui`: `kb-banner`, `kb-chip`, `kb-empty-state`, `kb-skeleton`, `kb-upload-zone`, `kb-avatar`, `kb-page-header`, `kb-lockup`, and `kb-segmented-nav` for the views of one thing, as router links in the page header. Pages start with a `kb-page-header`.
- A sheet's fields are `Field`s (`shared/ui/field.ts`): the value, the problem the form finds once the field was left or the form sent, and the one the API found, until the value changes.
- A filter toggle is a `button.kb-filter-chip` with `aria-pressed`.
- Times go through the `relativeTime` pipe, with `fullDate` in a tooltip (docs/UI_UX_SPEC.md §11): relative under 7 days, the date after that.
- An effect that starts a load wraps it in `untracked`, or the signals the load reads start it again, without end.
- Fonts and icons are self-hosted, because the app runs on the LAN. Nothing comes from Google Fonts or a CDN.
- Text is in Romanian, in sentence case, and goes through `i18n`: the `i18n` attribute in templates, `$localize` in code. Dates and numbers format as `ro-RO`.

## Projects

- `/projects` lists what the API lets the user open, for anyone signed in. `/projects/:id` opens on `board`, next to `list`, `members`, `reports` and `settings`; the board and the reports are placeholders until their groups, and the list is the tasks feature's page, which `projects.routes.ts` loads.
- `ProjectPage` provides one `ProjectStore` for its views, so they share the project and another project starts from nothing. `canManage` from the API (an owner, or `projects.manage`) shows Setări and the changes, and an archived project changes nothing until it's restored.
- The member sheet looks for users through `GET /api/projects/{id}/member-candidates`, so an owner without `users.manage` can still add someone.
- The last active owner's row has no ⋮, nor has a deactivated owner's while no owner is active: the API refuses to remove or demote them anyway (409 `Project.LastOwner`). `ProjectStore.keepsAnActiveOwnerWithout` decides it.

## Tasks

- A task opens in its panel, over the page it was opened from, through a router outlet of its own next to the page: `/projects/p1/list(panel:task/KAN-12)`. Any feature opens and closes it with `openTaskPanel` and `closeTaskPanel` (`shared/task-panel.ts`), which keep the page's query, so features don't import the tasks feature. `/tasks/KAN-12`, the address a task is shared by, finds its project and opens it over the project's board.
- `TaskPanelRoute` is what the outlet shows: it opens `TaskPanel` as a `MatDialog` with `taskPanelConfig` (920px, from the right, the window's height less 12px each side), and the outlet closes with the panel. Leaving the address, the back button for one, closes the panel.
- `TaskPanelStore` reads the task by its key, with `includeArchived` for `tasks.archive`, then the project's columns and members when the caller can change it (`canChange` from the API). Every change answers with the task as saved; a 409 reads it again, a refused field comes back by name and the panel says it in Romanian (`task-fields.ts`). `TaskChanges.version` counts the changes, so a list behind the panel reads its page again.
- A deadline is a day: the panel stores the end of that day where the person is (`endOfDay`), so a task due today isn't late until midnight. `kb-deadline-chip` (`shared/ui`) colors it as UI spec §6.5 says, neutral once the task is done.
- Markdown goes through `kb-markdown` (`shared/ui/markdown.ts`), which renders the tree `parseMarkdown` reads through templates. Nothing in the app sets `innerHTML` or bypasses the sanitizer: raw HTML is dropped, links go only to http, https and mailto, with `rel="noopener noreferrer"` (docs/SPEC.md §16). `scripts/check-tasks.sh 7` fails on any of it.
- The List view (`TaskListPage`) keeps its filters, sort and page in the address, so a list can be shared and the back button goes through them. The filters are the board's (docs/UI_UX_SPEC.md §7.4), in `shared/` for group 13: `TaskFilters` and their query (`shared/models/task-filters.ts`), and `kb-task-filter-bar` (`shared/ui`). "Ale mele" is `assignee=me`, the tasks of whoever opens the link. A row opens the panel and keeps the query.
- The headers sort through buttons of their own with a 12px chevron (UI spec §6.4), not `MatSort`, whose arrow Material draws.
- `mat-select` takes an option whose value is `null` for no choice at all and shows nothing, so "Neasignat" and "Oricine" have the value `''`.

## Access rules

- Hiding something in the UI is not access control: the backend enforces permissions.
- Show or hide based on permissions, never role names: `*kbHasPermission="'users.manage'"`, with the names in `PERMISSIONS` (`core/auth/permissions.ts`).
