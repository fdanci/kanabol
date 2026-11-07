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
- Run the acceptance checks of the skeleton and authentication, from the repo root: `scripts/check-web.sh [task-number...]`, those of the users and roles pages: `scripts/check-admin.sh [task-number...]`, those of the projects: `scripts/check-projects.sh [task-number...]`, those of the tasks: `scripts/check-tasks.sh [task-number...]` (7 and 8 need only the packages), those of the board: `scripts/check-board.sh [task-number...]`, those of several assignees: `scripts/check-assignees.sh [task-number...]` (4 and 5 need only the packages), and those of real time: `scripts/check-realtime.sh [task-number...]` (3 needs only the packages). Task 3 of check-admin.sh and tasks 1 and 2 of check-projects.sh also start the real API, so they need Docker and PostgreSQL like `scripts/check-users.sh`.

## How a request goes

- `correlationInterceptor` gives each API request an `X-Correlation-Id`. `errorInterceptor` shows a failed request in the error snackbar, with the end of that id, and passes the error on. It leaves a 401 to the session and a 400 with field errors to the form. A request that shows its own errors sets the `QUIET` context token, and one that shows only some codes itself, such as an email already taken under its field, passes `handledHere('User.EmailTaken')` as its context. The refusals of `docs/SPEC.md` §4.4 have Romanian messages in `BY_CODE`.
- `authInterceptor` sends the access token. On a 401 it waits for one refresh, shared with whatever failed at the same time, and sends the request once more. If the session is over, the user goes to the login page.
- `AuthStore` keeps the access token in memory only, never in storage. At startup it gets a new one from the refresh cookie, before the first page. Tabs take turns to refresh, through `navigator.locks`: two tabs sending the same cookie at once would look like a stolen token to the API and end the session. The cookie is the browser's, shared by every tab, so after someone else signs in elsewhere a refresh here answers for them: when its token's `sub` isn't the signed-in user, this tab ends its session instead of acting as them.
- `authGuard` opens a page to someone signed in, and only the password change page while `mustChangePassword` is on. `permissionGuard` is a `canMatch`: without the permission, the route after it on the same path matches and shows the 403 page at the address asked for. `withPermission` in `app.routes.ts` adds both.
- `connectionInterceptor` sends the tab's hub connection id in `X-Connection-Id`, from `TabConnection`, when it has one (below).
- `Logger` is the only code that writes to the console. `GlobalErrorHandler` logs what nobody caught. In production the Logger also hands warnings and errors to `ClientLogs`, which sends them to `POST /api/client-logs` for Seq: gathered for 5 seconds, at most 10 a minute, quietly, and only while signed in, since a 401 there would end on the login page.

## Real time

- `Realtime` (`core/realtime`) holds the one connection to `/hubs/board` (docs/SPEC.md §9), made by `HUB_LINK_FACTORY` with `@microsoft/signalr`, over WebSockets only, as the API offers, after a negotiation that gives the connection id. It opens when the first board joins a project and closes when the session ends. SignalR reconnects by itself, after 0, 2, 10 and 30 seconds, and when it gives up, or can't start, `Realtime` starts it again after 1, 5, then every 30 seconds; after either, it joins its projects again and calls the `onReconnected` handlers, who read what they missed. `JoinProject` answers null for a join and the code for a refusal, such as `Project.MemberRequired`, which goes to the log as a warning: the board says so itself when it reads.
- The token goes in the hub's address, so the connection asks `AuthStore.freshToken()` each time it connects: the token, or a refreshed one when it has 30 seconds or less left, counted from its own lifetime (`exp` less `iat`) since it came, as this computer's clock may be off the API's. The API closes a connection when its token expires, with leave to reconnect, which SignalR does with a fresh one.
- `on('TaskMoved', handler)` gives the payload typed by the event's name (`BoardEvents` in `core/realtime/models.ts`) and returns the function that stops listening. Only the board listens in v1, not the List or the panel.
- The connection's id lives in `TabConnection`, which `connectionInterceptor` reads, so the API's requests don't pull SignalR into the first bundle: it comes in the board's chunk.
- `HubLog` passes SignalR's log to `Logger`, with `access_token` hidden: warnings and errors as warnings, since a lost connection comes back by itself, critical ones as errors, the rest as debug.
- Specs stand in for the connection with a `HubLink`, and for the whole service with `FakeRealtime` (`realtime-testing.ts`), which any spec that shows the board provides: `provideFakeRealtime()`.

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
- Shared pieces live in `shared/ui`: `kb-banner`, `kb-chip`, `kb-empty-state`, `kb-skeleton`, `kb-upload-zone`, `kb-avatar`, `kb-avatar-stack` (the people of a task, overlapping, at most three and "+N", every name in its tooltip and `aria-label`), `kb-page-header`, `kb-lockup`, and `kb-segmented-nav` for the views of one thing, as router links in the page header. Pages start with a `kb-page-header`.
- A sheet's fields are `Field`s (`shared/ui/field.ts`): the value, the problem the form finds once the field was left or the form sent, and the one the API found, until the value changes.
- A filter toggle is a `button.kb-filter-chip` with `aria-pressed`.
- Times go through the `relativeTime` pipe, with `fullDate` in a tooltip (docs/UI_UX_SPEC.md §11): relative under 7 days, the date after that.
- An effect that starts a load wraps it in `untracked`, or the signals the load reads start it again, without end.
- Fonts and icons are self-hosted, because the app runs on the LAN. Nothing comes from Google Fonts or a CDN.
- Text is in Romanian, in sentence case, and goes through `i18n`: the `i18n` attribute in templates, `$localize` in code. Dates and numbers format as `ro-RO`.

## Projects

- `/projects` lists what the API lets the user open, for anyone signed in. `/projects/:id` opens on `board`, next to `list`, `members`, `reports` and `settings`; the reports are a placeholder until group 18, and the board and the list are other features' pages, which `projects.routes.ts` loads. On the board the page drops its width limit, since the board is full-bleed.
- `ProjectPage` provides one `ProjectStore` for its views, so they share the project and another project starts from nothing. `canManage` from the API (an owner, or `projects.manage`) shows Setări and the changes, and an archived project changes nothing until it's restored.
- The member sheet looks for users through `GET /api/projects/{id}/member-candidates`, so an owner without `users.manage` can still add someone.
- The last active owner's row has no ⋮, nor has a deactivated owner's while no owner is active: the API refuses to remove or demote them anyway (409 `Project.LastOwner`). `ProjectStore.keepsAnActiveOwnerWithout` decides it.

## Tasks

- A task opens in its panel, over the page it was opened from, through a router outlet of its own next to the page: `/projects/p1/list(panel:task/KAN-12)`. Any feature opens and closes it with `openTaskPanel` and `closeTaskPanel` (`shared/task-panel.ts`), which keep the page's query, so features don't import the tasks feature. `/tasks/KAN-12`, the address a task is shared by, finds its project and opens it over the project's board.
- `TaskPanelRoute` is what the outlet shows: it opens `TaskPanel` as a `MatDialog` with `taskPanelConfig` (920px, from the right, the window's height less 12px each side), and the outlet closes with the panel. Leaving the address, the back button for one, closes the panel.
- `TaskPanelStore` reads the task by its key, with `includeArchived` for `tasks.archive`, then the project's columns and members when the caller can change it (`canChange` from the API). Every change answers with the task as saved; a 409 reads it again, a refused field comes back by name and the panel says it in Romanian (`task-fields.ts`). `TaskChanges.version` counts the changes, so a list behind the panel reads its page again.
- A task has any number of assignees (docs/SPEC.md §4.4). The panel and the "Task nou" sheet pick them from a `mat-select` with `multiple`, its options and its `mat-select-trigger` showing avatars and names, "Neasignat" as the placeholder for nobody, the select growing a row when the names don't fit. The panel sends the whole list to `PUT /api/tasks/{id}/assignees` saved once, when the list closes, nothing when it closes as it was, and puts the ticks back when the API refuses. A deactivated assignee can be taken off, but not ticked again. `MatSelectHarness.close` clicks the first backdrop of the page, which in a sheet or the panel is the dialog's, so the specs close a list with Esc on its trigger. `assigneesChange` (`task-history.ts`) writes an `Assignees` history entry with names, «Ana → Ana, Dan», for the timeline of group 16.
- The panel's tabs are a `mat-tab-group`, the only one in the app (UI spec §6.7): 36px, 13px/500 labels, a 2px `accent-600` indicator, through `mat.tabs-overrides` in `styles.scss`. "Comentarii" is `kb-task-comments`, "Atașamente" `kb-task-attachments`; "Istoric" waits for group 16.
- `kb-task-comments` reads the task's comments through its own `TaskCommentsStore` (`CommentsApi`), from the oldest, and again when the panel shows another task. Each is an avatar, a name, a relative time with the full date in its tooltip, "editat" once edited, and its text through `kb-markdown`. "Editează" turns the caller's own into a field in place, when the API says `canEdit` and the caller can comment. Whoever can change the task has a form at the end, sent by its button or Ctrl+Enter; it keeps its text until the API takes it, and refuses more than 5,000 characters itself. A new comment counts on the board's card, so it tells `TaskChanges`.
- `kb-task-attachments` lists the task's files from its DTO, each downloaded under its name: the token is in memory, so `AttachmentsApi.download` reads the file as a blob and a link the page clicks saves it. Whoever can change the task gets `kb-upload-zone`; each file chosen goes up in a request of its own, as multipart in the field `file`, with a row and its progress until the API has it, then `TaskPanelStore.attached` puts it at the end of the list and tells `TaskChanges`. A file over 25 MB is refused in its row without being sent, one the API refuses says so in its row, and taking a row away stops its upload. A file whose `canDelete` the API gives is deleted after `Confirmation` asks.
- `HttpClient` runs on XHR (`withXhr()` in `app.config.ts`): Angular 22's default, fetch, can't report how far an upload went. `ApiClient.upload` sends a form with `reportUploadProgress`, and `ApiClient.blob` reads a file.
- A deadline is a day: the panel stores the end of that day where the person is (`endOfDay`), so a task due today isn't late until midnight. `kb-deadline-chip` (`shared/ui`) colors it as UI spec §6.5 says, neutral once the task is done.
- Markdown goes through `kb-markdown` (`shared/ui/markdown.ts`), which renders the tree `parseMarkdown` reads through templates. Nothing in the app sets `innerHTML` or bypasses the sanitizer: raw HTML is dropped, links go only to http, https and mailto, with `rel="noopener noreferrer"` (docs/SPEC.md §16). `scripts/check-tasks.sh 7` fails on any of it.
- The List view (`TaskListPage`) keeps its filters, sort and page in the address, so a list can be shared and the back button goes through them. The filters are the board's (docs/UI_UX_SPEC.md §7.4), in `shared/` for group 13: `TaskFilters` and their query (`shared/models/task-filters.ts`), and `kb-task-filter-bar` (`shared/ui`). "Ale mele" is `assignee=me`, the tasks of whoever opens the link. A task passes the assignee filter when that person is among its assignees, and its row shows them all in a `kb-avatar-stack`, or "Neasignat". A row opens the panel and keeps the query.
- The headers sort through buttons of their own with a 12px chevron (UI spec §6.4), not `MatSort`, whose arrow Material draws.
- `mat-select` takes an option whose value is `null` for no choice at all and shows nothing, so "Neasignat" and "Oricine" have the value `''`.

## Board

- `board-store.spec.ts` holds the store's moves and rollbacks against a fake API whose answers the specs hold and let go, and `board-moves.spec.ts` the pure `moveCard` and `placeAmong`; `board-page.spec.ts` drives the page, drops included, through the column's `dropped` output, with the fixtures in `board-testing.ts`.
- `BoardPage` (`features/board`) reads `GET /api/projects/{id}/board`: the columns and every open task, without pages, since the board shows them all. `BoardStore` holds it as a `resource()` of the project's id, with the members as another, and keeps the old board while it reads it again, so the cards don't blink.
- Cards move with CDK drag and drop: `cdkDropListGroup` on the board, a `cdkDropList` per column (`BoardColumn`), `cdkDrag` on each `TaskCard`. A drop moves the card at once (`moveCard` in `board-moves.ts`, which numbers the columns 0, 1, 2... as the API does), then sends `PATCH /api/tasks/{id}/position`; when the API refuses, the card goes back to its place and the board is read again. `errorInterceptor` has shown why by then.
- The CDK gives the place among the cards a column shows; `placeAmong` turns it into the place among all its cards, which the filters may hide some of.
- The card's look is on an inner `.card`, because the CDK moves the dragged copy with a `transform` on the host: the tilt of a picked-up card (UI spec §6.0.10) goes on the inner box, as do the placeholder's outline and the 200ms reordering.
- Dragging and "+ Task" follow `canMove` from the API, a real member of an open project, and "+ Task" also `tasks.create`. "+ Task" opens a title field in the column; Enter creates the task at its end, with the default priority, and the field stays for the next one.
- From the keyboard (UI spec §7.4): Space on a focused card picks it up, the arrows move it to another column, keeping its row as far as that column goes, or up and down, Space lets it go and Esc puts it back. Only letting go asks the API. `LiveAnnouncer` says each step in Romanian, every card is described by the keys' help, and the focus follows the card as it is drawn in its new column (`afterNextRender`).
- The filters are the List view's (`kb-task-filter-bar`, `TaskFilters`) and live in the address the same way, without the archived ones. They work in the browser on the cards the board has (`cardFilter` in `board-filters.ts`), a card passing the assignee filter when that person is among its assignees; a column counts the cards it shows. A card shows its assignees in a `kb-avatar-stack`, or an empty dashed avatar for nobody. A card dropped or let go among filtered cards lands just before the card it went above, or just after the last one shown, so the hidden ones keep their places.
- For `canManage` (an owner or `projects.manage`, open project) each column has a ⋮ (`kb-menu`): "Redenumește" turns the name into a field in place (Enter and leaving it save, Esc gives up), "Mută la stânga" and "Mută la dreapta" where there is room, "Marchează ca terminată" or back, which reads the board again since its tasks change, and "Șterge" only for an empty column that isn't the last, after `Confirmation.ask`. A column with only archived tasks gets 409 `Column.NotEmpty`, which `BY_CODE` says in Romanian. "+ Coloană" at the right end opens a name field. The ⋮ is 28px with a 16px icon, from `mat.icon-button-overrides` on `kb-board-column` in `styles.scss`.
- A card opens its panel with `openTaskPanel`, keeping the query, and `TaskChanges` (`shared/`, so the board needn't import the tasks feature) makes the board read itself again after the panel saves.
- What others change comes through `Realtime` (docs/SPEC.md §9, UI spec §7.4). `BoardStore` joins its project when it opens it and leaves it when the page goes, and ignores what comes for another project, and what its own user did from this tab: the event's `originConnectionId` is this tab's and its `byUserId` the signed-in user, since anyone on the project sees the tab's id and could send it in `X-Connection-Id`. `TaskMoved` moves the card in place, and `BoardPage` slides it there in 200ms: a copy of its box, fixed to the window so no column clips it, goes from the old place to the new while the card waits under it. `TaskCreated`, `TaskUpdated` (`Attachments` among its fields, for a file attached or taken off), `TaskArchived`, `TaskRestored` and `CommentAdded` make the board read itself again quietly, as does a reconnection: the counts on the cards come with the board. Every card someone else changed pulses once with `--kb-tint-100` in 600ms, through `pulses` in the store and the card's `pulse-a` and `pulse-b` classes, two names so the next change starts it again. Under `prefers-reduced-motion` neither moves.
- While a card is in hand, dragged (`dragStarted` and `dragEnded` of the column) or picked up from the keyboard, while a move of this tab's waits for the API, or while the first read is on its way, `BoardStore.hold`, `moving` and the read's state make what others do wait; the board then reads itself once, so nothing moves under the cursor and no read from before a move puts a card back.

## Access rules

- Hiding something in the UI is not access control: the backend enforces permissions.
- Show or hide based on permissions, never role names: `*kbHasPermission="'users.manage'"`, with the names in `PERMISSIONS` (`core/auth/permissions.ts`).
