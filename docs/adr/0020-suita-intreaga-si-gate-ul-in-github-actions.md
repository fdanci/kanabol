# 0020. Suita întreagă și gate-ul în GitHub Actions

## Decizie
Suita întreagă și gate-ul întreg rulează în GitHub Actions, nu pe mașina de lucru. Hook-urile git (Husky și lint-staged) fac doar verificări rapide, aceleași pe orice branch, `main` inclus.

- La un push pe `dev` sau pe un branch de lucru rulează treapta scurtă: testele, lint-ul și scripturile de verificare schimbate de push.
- Gate-ul întreg, adică toate verificările de acceptanță, rulează doar la push pe `main`, adică atunci când `dev` intră în `main`. Nu se pornește de mână și nu are alt declanșator.
- Un tag de versiune se pune doar pe un `main` al cărui gate e verde. Un gate roșu se repară în `dev`, care intră din nou în `main`.

## Motiv
- Gate-ul local al grupului 23 a ținut 172 de minute. Pe GitHub, în joburi paralele, ține 17.
- Mașina de lucru pică uneori sub încărcarea suitei, deci un gate lung se relua des.
- Cu suita în hook, un merge în `main` ținea minute întregi.
- Repo-ul e public, deci mașinile standard ale GitHub sunt gratuite.

## Alternative
- Suita întreagă în hook-uri pe `main`, cum era până la 2026-11-12: lentă și legată de starea mașinii de lucru.
- Gate-ul întreg pornit de mână pe `dev`, înainte de merge: o regulă în plus de ținut minte, pentru o verificare pe care push-ul pe `main` o face oricum.
- Gate-ul întreg la fiecare push: zece joburi pentru fiecare commit, fără să prindă mai mult decât treapta scurtă și gate-ul de pe `main` împreună.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]] · [[Kanban pe steroizi/Decisions/Husky și lint-staged la commit|Husky și lint-staged la commit]] · [[Kanban pe steroizi/Decisions/Postgres real în teste|Postgres real în teste]]
