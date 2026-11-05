# 0008. Husky și lint-staged la commit

## Decizie
Verificările rulează local, în hook-uri git (Husky și lint-staged), fără server de CI. Suita completă rulează la commit sau merge pe `main`, iar merge-ul în `main` e mereu `--no-ff`.

## Motiv
- Nu există runner, iar testele trebuie să ruleze mereu înainte de un commit pe `main`.
- Fast-forward nu rulează niciun hook, de aceea `--no-ff` e obligatoriu.
- Pe feature branches rulează doar verificări rapide, ca să nu încetinească munca.
- `git commit --no-verify` rămâne posibil, așa că `pre-push` pe `main` e plasa de siguranță.

## Alternative
- Server de CI (GitHub Actions, Gitea): dacă apare mai târziu, rulează același `scripts/test-all.sh`.
- Suita completă la fiecare commit, pe orice branch: ar încetini munca pe feature branches.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Postgres real în teste|Postgres real în teste]]
