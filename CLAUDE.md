# Kanabol

## Project notes

The tasks and documentation live in an Obsidian vault, in `C:\Users\Florin\Documents\Obsidian Vault\Efforts\Projects\Kanban pe steroizi`:

- `Tasks.md`: the tasks, under **Now**, **Next**, **Blocked** and **Done**. Read tasks from here.
- `_Overview.md`: what the project is and what the first version includes. This is Florin's definition, so ask rather than changing it.
- `_Architecture.md`: the high-level architecture. Details that change with the code belong in this repo.
- `Decisions/`: one note per important decision.

Follow the vault's own `CLAUDE.md` when editing notes.

## Spec

`docs/SPEC.md` is the technical spec: architecture, patterns, conventions, logging, tests, lint, Docker and the commit pipeline. It holds rules only: how a thing was built is in the code, its tests and the head of its `scripts/check-*.sh`. Before starting a task, read the section it points to. `docs/adr/` holds numbered copies of the vault's decision notes.

`docs/UI_UX_SPEC.md` is the visual spec: theme, components, screens and text. It wins over `docs/SPEC.md` on anything visual, so read the section a frontend task points to. It's a copy of the vault's `Atasamente/UI_UX_SPEC.md`.

## After implementing a feature

- Check the task off and move it to **Done** in `Tasks.md`.
- Update `_Architecture.md` or `Decisions/` if the feature changed or settled something they cover.
- Decide a small gap in the specs yourself and say what you chose in the commit message, without adding it to the spec. When the code changes a rule of `docs/SPEC.md`, change the rule in the same commit.
- Ask Florin only for product decisions, what a user sees or can do. Write under **Blocked** in `Tasks.md` only what really stops the work, not what is already implemented and waits for a yes.
- Only Florin and the supervisor edit `docs/UI_UX_SPEC.md`, `_Overview.md`, `_Architecture.md`, `Decisions/` and its copies in `docs/adr/`.

## Naming

A summary of `docs/SPEC.md` §7, which has the full tables and examples.

**C#**

- Namespaces are file-scoped: `Kanabol.<Layer>.<Folder>`. One public type per file, named after the type.
- PascalCase for types, methods, properties and constants. `I` prefix for interfaces, `_camelCase` for private fields, camelCase for parameters and locals.
- Commands and queries: `<Verb><Noun>Command`, `Get<Noun>[By…]Query`. Their handlers and validators add `Handler` and `Validator`: `CreateTaskCommandHandler`.
- `<Noun>Dto` for responses, `<Verb><Noun>Request` for request bodies, `<Noun><PastTenseVerb>Event` for domain events, a static `<Noun>Errors` for errors, `<Entity>Configuration` for EF.
- The `Async` suffix is only for infrastructure interfaces. Handlers use `Handle`.
- Tests: `<Method>_<Scenario>_<Result>`.

**Database**

- `snake_case` throughout, with plural table names: `task_items`. The primary key is `id` (UUID v7), foreign keys are `<entity>_id`.
- Indexes are `ix_<table>_<columns>`, unique ones `ux_<table>_<columns>`.
- Timestamps are `timestamptz` and end in `_at`. Booleans start with `is_` or `has_`. Enums are stored as `text`.

**REST API**

- Base path `/api`, with plural kebab-case resources: `/api/projects/{id}/board-columns`. JSON is camelCase.
- Pages use `?page=1&pageSize=50` and return `Page<T>`.
- 200 for reads and updates, 201 with `Location` for creates, 204 for deletes, ProblemDetails for 4xx.
- SignalR hubs live under `/hubs`. The correlation header is `X-Correlation-Id`.

**Angular**

- No `.component` or `.service` file suffixes, and the selector prefix is `kb`: `task-card.ts` holds `TaskCard`, selector `kb-task-card`.
- Pages are `board-page.ts` / `BoardPage`, stores `board-store.ts` / `BoardStore`, API services `tasks-api.ts` / `TasksApi`, guards and interceptors `auth-guard.ts` / `authGuard`.
- Signals have no prefix or suffix, outputs have no `on` prefix, and global constants are `UPPER_SNAKE`.

**Git**

- Branches: `feature/<kebab>`, `fix/<kebab>`, `chore/<kebab>`, `docs/<kebab>`.
- Conventional Commits in English: `feat(board): drag and drop between columns`. Scopes: `api`, `web`, `db`, `auth`, `board`, `tasks`, `infra`, `ci`, `docs`.

## Git

Never mention Claude in commit messages or PRs, and never add a `Co-Authored-By` line or any other AI attribution.

After cloning, run `scripts/setup.sh`. It installs the packages, turns on the hooks and makes every merge into `main` create a commit. The hooks run the same fast checks on every branch, `main` included, and need no Docker (`docs/SPEC.md` §14). The full suite runs in the GitHub workflow (`docs/ci.md`), or by hand with `scripts/test-all.sh`, which needs Docker Desktop running.

Feature branches go into `dev`, and `dev` into `main`. Each group in `Tasks.md` has its acceptance checks in `scripts/check-*.sh`, and they run on GitHub, not on your machine (`docs/SPEC.md` §14.7). A group is done when the short tier is green on its branch: `check-tests.sh`, `check-lint.sh` and each `check-*.sh` the push changed. The full gate, every `check-*.sh` through `scripts/gate.sh`, which does their shared build, tests and lint once, runs only on a push to `main`, when `dev` is merged, and a tag goes only on a green `main`.

## CI

`.github/workflows/ci.yml` runs on GitHub's `ubuntu-latest` machines: on a push to `dev`, `feature/*`, `fix/*` or `chore/*`, `check-tests.sh`, `check-lint.sh` and the `check-*.sh` the push changed; on a push to `main` and when started by hand, the full gate (`docs/ci.md`, `docs/SPEC.md` §14.8). A new `check-*.sh` goes into one of the shards in `scripts/ci-plan.sh`.

## The server

`docs/deploy.md` installs Kanabol on the server and updates it: the clone, `.env`, `scripts/make-certs.sh`, `scripts/release.sh vX.Y.Z` (the images, built on the server, with no registry), `compose up` with `compose.prod.yaml`. `docs/backup.md` has the nightly `scripts/backup.sh` and `scripts/restore.sh`. `scripts/demo-data.sh` fills a running Kanabol with demonstration data through its API. None of them prints a password, and only `restore.sh` asks anything. `scripts/check-release.sh` checks them all (`docs/SPEC.md` §14.7).

The commit a version tags sets that version in `APP_VERSION` (`frontend/src/app/core/layout/sidenav.ts`, what the sidebar shows) and in the default tag of both images in `compose.prod.yaml`; `release.sh` refuses a tag that doesn't match them (`docs/SPEC.md` §13.5).
