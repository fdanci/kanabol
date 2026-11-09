# Verificarea UI (grupul 19b)

Fiecare ecran din `docs/UI_UX_SPEC.md` §7, în light și în dark, verificat cu `axe-core` și pentru `prefers-reduced-motion` (UI §8, §9, §15; `docs/SPEC.md` §13.5). Făcută pe 2026-11-09, pe `feature/delivery`, și reluată în grupul 20, pe `feature/collation`, după token-urile de contrast (mai jos).

## Cum

- **Datele**: `scripts/demo-data.sh` pe un API local în Production, pe o bază proprie, cu verificarea întârziatelor la un minut, ca notificările „a depășit deadline-ul” să existe. Aplicația din `ng serve`, cu proxy spre acel API. Adminul e cel care se uită, cu 13 notificări necitite.
- **axe-core** 4.14 (dependență de dev în `frontend/package.json`), rulat în Browser pane pe fiecare ecran, cu regulile `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa` și `best-practice`. Tema vine din `prefers-color-scheme`, pe care aplicația o urmează cât omul nu a ales una. Viewport 1440×900.
- **Capturile**: PNG de 1440×900 în `docs/screens/`, `<secțiune>-<ecran>-<temă>.png`, dintr-un Edge headless condus prin protocolul DevTools, pe aceleași date, după reparațiile de mai jos. Refăcute în grupul 20 cu token-urile noi, pe aceleași date.
- **Mișcarea**: în Browser pane (care raportează `reduce`), fiecare element cu o tranziție mai lungă de 0 și fiecare animație care rulează; apoi în Edge headless, cu API-ul încetinit, `reduce` față de `no-preference`.

## Ecranele

| UI §7 | Ecranul                                  | Capturile                                                                       |
| ----- | ---------------------------------------- | ------------------------------------------------------------------------------- |
| 7.1   | Login                                    | `7.1-login-light.png`, `7.1-login-dark.png`                                     |
| 7.1   | Schimbarea obligatorie a parolei (Ioana) | `7.1-parola-noua-*.png`                                                         |
| 7.2   | Panou principal                          | `7.2-panou-*.png`                                                               |
| 7.2   | Task-urile mele                          | `7.2-task-urile-mele-*.png`                                                     |
| 7.3   | Proiecte                                 | `7.3-proiecte-*.png`                                                            |
| 7.3   | Proiect: Listă, Membri, Rapoarte, Setări | `7.3-lista-*.png`, `7.3-membri-*.png`, `7.3-rapoarte-*.png`, `7.3-setari-*.png` |
| 7.4   | Board                                    | `7.4-board-*.png`                                                               |
| 7.5   | Panoul task-ului, peste board            | `7.5-task-*.png`                                                                |
| 7.6   | Meniul clopoțelului și pagina Notificări | `7.6-notificari-meniu-*.png`, `7.6-notificari-*.png`                            |
| 7.7   | Utilizatori și Roluri                    | `7.7-utilizatori-*.png`, `7.7-roluri-*.png`                                     |
| 7.8   | Rapoarte                                 | `7.8-rapoarte-*.png`                                                            |

## Ce a găsit axe-core, înainte de reparații

Aceleași reguli în light și în dark:

| Regula                                 | Impact   | Unde                                                                                                         |
| -------------------------------------- | -------- | ------------------------------------------------------------------------------------------------------------ |
| `color-contrast`                       | serious  | toate ecranele cu shell: textul în `--kb-text-500` la 12–13px (detaliat mai jos)                             |
| `color-contrast`                       | serious  | 7.3 Membri, doar în dark: chip-ul „Owner”, `text-900` pe `--kb-sky-300` (#eef2ff pe #3d6fc9, 4,35:1)         |
| `region`                               | moderate | toate ecranele cu shell: logo-ul din navbar și „Versiunea 0.1.0” din sidebar, în afara oricărui landmark     |
| `heading-order`                        | moderate | 7.4 Board: coloanele erau `h3` sub `h1`-ul paginii                                                           |
| `label-content-name-mismatch`          | serious  | 7.4 Board: numele accesibil al cardului e „PORT-2 Pagina de facturi…”, fără prioritate, deadline și asignați |
| `aria-prohibited-attr` (de verificat)  | —        | 7.4 Board: numărul coloanei, un `span` cu `aria-label`; 7.3 Listă: iconița „Gata”, la fel                    |
| `aria-valid-attr-value` (de verificat) | —        | 7.1 Login și Schimbarea parolei: `aria-describedby` spre erori care nu sunt încă pe pagină                   |
| `region`                               | moderate | 7.5 și 7.6 meniul: overlay-ul Material (dialogul, meniul) e lângă `body`, în afara landmark-urilor           |

Mișcarea: cu `reduce`, câmpurile, `kb-filter-chip`, select-urile, rândurile tabelelor, link-urile din sidebar și iconițele păstrau tranziția de 120 ms (UI §8 cere 0 ms pentru toate). Shimmer-ul se oprea deja.

## Ce am reparat

| Problema                                   | Reparația                                                                                                                  | Verificată de                                                |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| Tranziții de 120 ms sub `reduce`           | `styles.scss`: sub `prefers-reduced-motion: reduce`, `transition-duration` și `transition-delay` 0 ms pe tot, `!important` | `check-release.sh` 3 în CSS-ul construit; Edge: 0,12 s → 0 s |
| Logo-ul în afara landmark-urilor           | `kb-navbar` are `role="banner"`                                                                                            | `layout.spec.ts`                                             |
| Versiunea în afara landmark-urilor         | „Versiunea 0.1.0” e un `footer`                                                                                            | `layout.spec.ts`                                             |
| Coloanele board-ului `h3` sub `h1`         | `h2`, cu același stil (`kb-subtitle`)                                                                                      | `board-page.spec.ts`                                         |
| `aria-label` pe numărul coloanei           | cifra e `aria-hidden`, iar „3 task-uri” e text ascuns lângă ea                                                             | `board-page.spec.ts`                                         |
| `aria-label` pe iconița „Gata” din listă   | `role="img"`, ca iconița din capul coloanei                                                                                | `task-list-page.spec.ts`                                     |
| `aria-describedby` spre erori care lipsesc | doar cât eroarea e pe pagină; parola nouă rămâne descrisă de reguli                                                        | `auth-pages.spec.ts`                                         |

Nicio reparație nu schimbă ce spun specurile. După ele, pe fiecare ecran, în ambele teme, au rămas doar cele de mai jos.

## Ce a rămas după 19b (în Blocked atunci), și cum s-a decis

1. **`--kb-text-500` la 12–13px.** UI §2.2 îl permitea doar pentru text de cel puțin 14px bold sau 18px, dar §6.0.7 (titlurile grupurilor din sidebar), §6.3 (textul de ajutor) și §7.4 (cheia de pe card) îl cer la mărimea `caption`, iar codul îl folosește la fel pentru meta: cheile task-urilor (card, Listă, Task-urile mele, panou), timpii relativi, cheile proiectelor, versiunea. Măsurat: în light 4,05:1 pe alb, 3,98 pe sidebar (#fcfdff), 3,81 pe zebra tabelelor (#f7f8fa), 3,65 pe rândurile necitite (#ecf4fc); în dark 3,99 pe #141c32, 3,86 pe #161f36, 3,59 pe #1c253c, 3,33 pe #1e2a4a. Decis în UI v1.20: `--kb-text-500` #697078 în light (4,51–5,01:1 pe aceleași fundaluri) și #8692ae în dark (4,54–5,77:1), tot sub `text-600`, deci ierarhia rămâne. Aplicat în grupul 20.
2. **Chip-ul „Owner” în dark**: `text-900` pe `--kb-sky-300` #3d6fc9, 4,35:1 la 12px. Decis în UI v1.20: `--kb-sky-300` în dark #3b6cc7 (4,52:1), `--kb-chart-2` rămâne #3d6fc9. Aplicat în grupul 20.
3. **Numele cardului de pe board** (`label-content-name-mismatch`, regulă experimentală în axe 4.14): numele e cheia și titlul, iar cardul arată și prioritatea, deadline-ul și inițialele asignaților. Regula pică și cu tot textul vizibil copiat în nume, deci nu poate trece din `aria-label`. Decis în UI v1.20 (§7.4): rămâne cheia și titlul, tastele în `aria-describedby`.
4. **Overlay-urile Material** (`region` la dialog și meniu): containerul overlay-ului CDK stă lângă `body`, cum le pune Material. Decis în UI v1.20 (§6.0.5, §6.0.6): acceptat, ca celelalte excepții Material.

## Grupul 20: token-urile de contrast

Pe 2026-11-09, pe `feature/collation`, cu `--kb-text-500` #697078 / #8692ae și `--kb-sky-300` #3b6cc7 în dark (UI §2.1–§2.3, §3.2) în `_tokens.scss`. Aceleași date ca în 19b (`scripts/demo-data.sh` pe un API local în Production, adminul cu 13 notificări necitite), aplicația din `ng serve`, axe-core 4.14 în Browser pane, la 1440×900, cu tema din `prefers-color-scheme`.

| Ecranul                        | Regulile                                  | Light                                                                                              | Dark                                                             |
| ------------------------------ | ----------------------------------------- | -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| 7.2 Panou principal (cu shell) | toate, ca în 19b                          | 0 încălcări                                                                                        | 0 încălcări                                                      |
| 7.3 Membri (`7.3-membri`)      | toate, ca în 19b                          | 0 încălcări; „Owner” `#052063` pe #91c4fb                                                          | 0 încălcări; „Owner” `#eef2ff` pe #3b6cc7                        |
| Toate cele 13 ecrane cu shell  | `color-contrast`                          | 0 încălcări                                                                                        | 0 încălcări                                                      |
| Contraprobă: valorile vechi    | toate, valoarea pusă pe `:root` în pagină | Panou cu #787f86: 15 noduri `color-contrast` (titlurile grupurilor din sidebar, versiunea, cheile) | Membri cu #3d6fc9: cele 2 chip-uri „Owner”; cu #6f7b96: 3 noduri |

Cele 13 ecrane cu shell: Panou, Task-urile mele, Proiecte, proiectul cu Listă, Membri, Rapoarte, Setări și Board, Notificări, Utilizatori, Roluri, Rapoarte și panoul task-ului. Rămân „de verificat”, ca în 19b, doar butoanele pline cu gradient (`kb-default`), verificate de mână mai jos.

Capturile din `docs/screens/` sunt refăcute cu token-urile noi, pe aceleași date, cu același Edge headless: 30 diferă de cele din v0.1.0. Cele două ale paginii de login nu au text în `text-500` și au ieșit identice, octet cu octet, deci în rest nimic nu s-a mișcat. `check-collation.sh` 3 verifică token-urile, CSS-ul construit, capturile și acest document.

## Verificate de mână (axe le-a lăsat „de verificat”)

- Butoanele pline (`kb-default`, `kb-danger`): gradient, deci axe nu află fundalul. UI §2.2 le dă 5,4 și 6,6:1 (4,8 și 5,8 în vârful gradientului).
- Meniul clopoțelului pe sticlă (`kb-glass`): `text-600` pe panou (alb 72% în light, #161f36 72% în dark): ~5,4:1 în light; 6,9:1 în dark, 5,8:1 pe un rând necitit (#1e2a4a).
- Valoarea din `mat-select`: `text-700` pe fundalul câmpului.
- `aria-hidden-focus` în spatele panoului task-ului: Material ascunde pagina de sub un dialog modal și ține focusul în el.
- `aria-controls` al clopoțelului: id-ul meniului există doar cât meniul e deschis.

## `prefers-reduced-motion`

Măsurat în Edge headless, pe panoul principal, cu API-ul încetinit la 3 s, ca skeleton-urile să rămână pe ecran:

|                                   | `no-preference`                 | `reduce`                               |
| --------------------------------- | ------------------------------- | -------------------------------------- |
| Skeleton-uri pe ecran             | 38, fiecare cu shimmer de 1,2 s | 38, nicio animație (`animation: none`) |
| Link-urile din sidebar, rândurile | 120 ms                          | 0 s                                    |
| Câmpuri, `kb-filter-chip`         | 120 ms                          | 0 s                                    |

În Browser pane, cu `reduce`, niciun element din cele 16 ecrane nu mai are o tranziție, și nicio animație nu rulează după încărcare. Board-ul nu alunecă și nu pulsează sub `reduce` (`board-page.ts`, grupul 14), iar Material își oprește singur animațiile.
