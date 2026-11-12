# CI

Hook-urile de pe mașina ta rulează doar verificări rapide, pe orice branch, `main` inclus. Suita întreagă, `scripts/test-all.sh`, și gate-ul întreg, toate `scripts/check-*.sh`, rulează în GitHub Actions, în `.github/workflows/ci.yml`. Regula e în `docs/SPEC.md` §14; pagina asta spune ce rulează când, cum pornești gate-ul de mână și cum citești un eșec.

## Regula pentru `main`

- `dev` intră în `main` doar după un gate întreg verde pe GitHub, pornit de mână pe commit-ul din `dev` care intră.
- Push-ul pe `main` rulează gate-ul întreg încă o dată, pe merge commit.
- Un tag de versiune se pune doar pe un `main` al cărui gate e verde.
- Merge-ul în `main` rămâne `--no-ff` (îl setează `scripts/setup.sh`), ca merge commit-ul să treacă prin verificările rapide ale hook-ului.

### Până la primul gate întreg verde pe GitHub

Până atunci, `dev` intră în `main` doar după ce `scripts/test-all.sh` rulează verde de mână, pe commit-ul din `dev`, cu Docker Desktop pornit:

```sh
git switch dev
scripts/require-docker.sh && scripts/test-all.sh
```

Înainte de un tag de versiune, în perioada asta, rulează și gate-ul întreg local, pe o clonă proaspătă:

```sh
scripts/gate.sh --fresh dev ../kanabol-gate ../kanabol-gate-logs
```

## Ce rulează când

| Când                                           | Treapta        | Ce rulează                                                                                        |
| ---------------------------------------------- | -------------- | ------------------------------------------------------------------------------------------------- |
| push pe `dev`, `feature/*`, `fix/*`, `chore/*` | scurtă         | `check-tests`, `check-lint` și fiecare `scripts/check-*.sh` schimbat de push, câte un job fiecare |
| push pe `main`                                 | gate-ul întreg | toate `scripts/check-*.sh`, în 10 joburi paralele                                                 |
| pornit de mână, pe orice branch                | gate-ul întreg | la fel                                                                                            |

Scriptul grupului nu se poate ghici în CI, deci treapta scurtă nu-l rulează decât dacă push-ul îl schimbă; la un grup, treapta grupului (`docs/SPEC.md` §14.7) rămâne de rulat local. Pentru un branch nou, „ce a schimbat push-ul” înseamnă ce n-are `dev`.

Fiecare rulare are trei feluri de joburi:

1. **plan**: `scripts/ci-plan.sh` alege treapta și împarte scripturile pe joburi. Un script nou care nu e în niciun job al gate-ului întreg oprește planul: adaugă-l în lista din `scripts/ci-plan.sh`.
2. **prepare**: `scripts/gate-prepare.sh` face o dată ce au scripturile în comun: build-ul soluției, `dotnet format`, `ng lint`, `prettier --check`, build-ul de producție și `scripts/test-all.sh`, adică suita întreagă, cu rezultatele ei. Le dă joburilor de verificare prin cache-ul rulării, împreună cu `bin/` și `obj/` din backend.
3. **check (…)**: un job pe bucată, care rulează `scripts/gate.sh` cu scripturile bucății. Scripturile refolosesc ce a făcut prepare doar pentru comenzile identice, pe același arbore (`docs/SPEC.md` §14.7); contraprobele, API-ul real și Docker-ul rulează de-adevăratelea. Fără prepare (picat sau anulat), scripturile rulează fiecare cu munca lui, ca singure.

Joburile rulează pe mașini separate, deci nu-și împart portul, baza de date sau Docker-ul; într-un job, scripturile rulează unul după altul, fiecare cu portul și baza lui.

Un push nou pe același branch anulează rularea veche. Toate joburile rulează pe `ubuntu-latest`, gratuit pentru un repo public, cu acțiunile oficiale fixate pe commit și cu permisiunea `contents: read`. Nu există niciun secret: parolele scripturilor sunt valori de test din repo.

### Ce e altfel pe Linux

`.github/actions/setup/action.yml` pregătește fiecare job:

- .NET 10 și Node 22, cu cache-ul pachetelor NuGet și npm;
- `sh` devine bash, cum e și sub Git for Windows, ca scripturile să ruleze sub același interpretor ca local;
- un PostgreSQL 17 al jobului, pe portul 5433, în locale-ul C, cu acces `trust` doar de pe 127.0.0.1. `KANABOL_CHECK_DB`, `KANABOL_PGDATA` și `KANABOL_PGBIN` îl arată scripturilor, iar `check-collation.sh` 2 îl poate opri și porni cu `pg_ctl`. Portul 5432 rămâne liber pentru `check-docker.sh` 5;
- `scripts/setup.sh`.

Docker e deja pe mașină. Notele din vault nu sunt, iar `check-foundation` și `check-ui` sar peste verificările lor, cum scriu. Pe Windows totul rulează exact ca înainte.

## Gate-ul întreg, pornit de mână

Pe GitHub: **Actions** → **CI** → **Run workflow**, alegi branch-ul (`dev`, înainte de un merge în `main`) → **Run workflow**. GitHub arată butonul doar după ce `ci.yml` e pe branch-ul implicit, `main`; până atunci, și oricând altfel, un push al cărui ultim commit are `[full gate]` în mesaj rulează gate-ul întreg pe branch-ul lui (un commit gol ajunge: `git commit --allow-empty -m "chore(ci): run the full gate [full gate]"`). Sau, cu GitHub CLI:

```sh
gh workflow run ci.yml --ref dev
```

Gate-ul e verde când toate joburile `check (…)` sunt verzi. Commit-ul verificat e cel din capul branch-ului la pornire: un push în timpul rulării o anulează și pornește alta.

## Cum citești un eșec

1. În rularea picată, deschide jobul `check (…)` roșu. Pagina rulării are, pentru fiecare job, rezumatul lui `scripts/gate.sh`: `ok` sau `FAIL` pe script, secundele lui și câte răspunsuri au venit din cache.
2. Pasul `scripts/gate.sh …` arată același rezumat la final. Logurile întregi sunt în artefactul `logs-<bucată>` al rulării: `<script>.log` are fiecare verificare cu `ok` sau `FAIL` și, sub o verificare picată, ultimele rânduri ale ei.
3. Rulează scriptul picat local, singur, ca să vezi dacă pică și acolo: `sh scripts/check-<nume>.sh <task>`. Un script care pică doar în gate, cu cache-ul, și trece singur e o problemă a refolosirii: rulează gate-ul cu `KANABOL_GATE_NO_REUSE=1` și spune-o.
4. Dacă pică jobul `prepare`, artefactul `prepare-logs` are logul fiecărui pas (`build.log`, `test-all.log` și celelalte). Scripturile au rulat oricum, fără cache.
5. O limită de timp care nu ține pe mașinile GitHub (de exemplu un commit sub 40 s în `check-pipeline.sh` 6) nu se slăbește: notează timpul măsurat și propune o regulă în Blocked, în `Tasks.md`.

## Ce rulează hook-urile

`scripts/verify.sh`, la fel pe orice branch:

- lint-staged pe fișierele din index;
- cu o schimbare în `backend/`: build-ul cu avertismentele ca erori și testele de domeniu;
- cu o schimbare în `frontend/`: specurile.

La un merge, în index e tot ce aduce merge-ul. Nimic din toate astea nu are nevoie de Docker. Nu există hook `pre-push`: un commit făcut cu `--no-verify` îl prinde gate-ul care rulează la push pe `main`.
