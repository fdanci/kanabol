# 0008. Husky și lint-staged la commit

## Decizie
Verificările rulează local, în hook-uri git (Husky și lint-staged), fără server de CI. Munca merge feature branch → `dev` → `main`. Suita completă rulează la commit sau merge pe `main`, iar merge-ul în `main` e mereu `--no-ff` și se face doar după ce verificările de acceptanță ale grupului au trecut pe o clonă curată.

De la 2026-11-13, partea despre suita completă pe `main`, despre lipsa unui server de CI și despre `pre-push` e înlocuită de [[Kanban pe steroizi/Decisions/Suita întreagă și gate-ul în GitHub Actions|Suita întreagă și gate-ul în GitHub Actions]]: hook-urile fac doar verificări rapide pe orice branch, iar suita și gate-ul rulează pe GitHub. Rămân hook-urile, feature branch → `dev` → `main` și `--no-ff`.

## Motiv
- Nu există runner, iar testele trebuie să ruleze mereu înainte de un commit pe `main`.
- Fast-forward nu rulează niciun hook, de aceea `--no-ff` e obligatoriu.
- Pe feature branches și pe `dev` rulează doar verificări rapide, ca să nu încetinească munca. `dev` adună grupurile terminate între două merge-uri în `main`, deci `main` conține doar muncă verificată.
- `git commit --no-verify` rămâne posibil, așa că `pre-push` pe `main` e plasa de siguranță.

## Alternative
- Server de CI (GitHub Actions, Gitea): dacă apare mai târziu, rulează același `scripts/test-all.sh`.
- Suita completă la fiecare commit, pe orice branch: ar încetini munca pe feature branches.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Postgres real în teste|Postgres real în teste]], [[Kanban pe steroizi/Decisions/Suita întreagă și gate-ul în GitHub Actions|Suita întreagă și gate-ul în GitHub Actions]]
