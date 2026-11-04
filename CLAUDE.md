# Kanabol

## Project notes
The tasks and documentation live in an Obsidian vault, in `C:\Users\Florin\Documents\Obsidian Vault\Efforts\Projects\Kanban pe steroizi`:

- `Tasks.md`: the tasks, under **Now**, **Next**, **Blocked** and **Done**. Read tasks from here.
- `_Overview.md`: what the project is and what the first version includes. This is Florin's definition, so ask rather than changing it.
- `_Architecture.md`: the high-level architecture. Details that change with the code belong in this repo.
- `Decisions/`: one note per important decision.

Follow the vault's own `CLAUDE.md` when editing notes.

## After implementing a feature
- Check the task off and move it to **Done** in `Tasks.md`.
- Update `_Architecture.md` or `Decisions/` if the feature changed or settled something they cover.

## Git
Never mention Claude in commit messages or PRs, and never add a `Co-Authored-By` line or any other AI attribution.
