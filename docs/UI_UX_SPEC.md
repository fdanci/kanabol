# UI/UX SPEC — Kanabol

> Specificație vizuală și de interacțiune pentru frontend-ul Angular 22 + Angular Material 22. Complementară cu `SPEC.md` (arhitectură); unde se contrazic, pentru vizual câștigă acest fișier.
>
> Stare: v1.5 — 2026-11-05 (v1.4 + lectura cap-coadă: `--kb-ink` pentru tooltip și snackbar în dark, animația meniurilor în §8, zona de click a butoanelor-iconiță în §3.3, §6.4 și §9, căutarea globală scoasă din v1, tab „Rapoarte” pe proiect în §7.3, markdown sanitizat în §7.5, mărunțișuri în §0, §2.3, §6.0.4, §7.4, §13). Tema derivă din culorile aplicației interne existente la firmă (screenshot „SDG Platformă Web”, eșantionat pixel cu pixel), ca trecerea colegilor la Kanabol să fie familiară. Ce era inconsistent acolo e corectat aici, nu copiat. **Forma** (colțuri, borduri, umbre, dialoguri, controale) urmează limbajul aplicațiilor macOS — nu culorile lui, ci proporțiile și materialele.

![Paleta Kanabol](images/kanabol-palette.png)

---

## 0. Rezumat

| Subiect | Decizie |
|---|---|
| Direcție | „Enterprise calm, formă macOS”: alb + albastru-marin, fundal foarte deschis, accente albastre vii; colțuri rotunjite generos, borduri fine cu highlight, umbre moi stratificate, translucență pe sidebar/meniuri |
| Paletă | 6 culori de brand + 4 de stare + neutre; toate ca CSS custom properties `--kb-*` și ca temă Material M3 |
| Buton implicit (default) | **fill-accent `#135bf7` plin, text alb, gradient subtil** — ca butonul „default” din macOS. *(schimbat față de v1.0; sky-300 trece la selecție/highlight)* |
| Buton secundar (bezel) | alb, bordură `border-300`, umbră 1px — butonul standard macOS |
| Link / activ / focus | accent-600 `#135bf7`; focus = halo 4px semitransparent |
| Font | Inter (self-hosted `@fontsource-variable/inter`), 13px bază în controale, 14px în text, tracking ușor negativ pe titluri |
| Spațiere | scară de 4px: 4 / 8 / 12 / 16 / 24 / 32 / 48 |
| Rază colțuri | 6px controale, 8px carduri mici/chips pătrate, 10px carduri, 12px dialoguri/sheets/popovers, 999px pill |
| Layout | navbar 52px (toolbar translucid), sidebar 240px source-list (colapsabil la 56px), conținut max 1440px |
| Densitate Material | `-1` (compact) |
| Dark mode | suportat prin `prefers-color-scheme` + toggle manual; aceeași semantică de tokens |
| Iconițe | Material Symbols Outlined, self-hosted, greutate 400; 16px în controale, 20px în liste, 48px în empty states (§12) |
| Logo | **Ganteră cu discuri-carduri** (concept #02), mark + wordmark „Kanabol” în Inter 700; fișiere în `Atasamente/logo/` și `frontend/public/brand/` (§14) |
| Limbă | UI în română, sentence case, fără majuscule decorative |

---

## 1. Principii

1. **Familiar, nu identic.** Aceeași familie de culori ca softul actual, ca să nu pară „un alt tool de învățat”. Dar: aliniere consistentă, spațiere regulată, un singur stil de buton primar, fără controale native nestilizate.
2. **Densitate de lucru.** Utilizatorii stau în aplicație ore întregi; preferăm mai multă informație pe ecran și mai puțin padding decât default-ul Material.
3. **Starea e vizibilă.** Orice acțiune are feedback în < 100 ms (optimistic update + snackbar); orice listă are stare de încărcare, goală și de eroare proiectată, nu improvizată.
4. **Tastatura contează.** Tot ce se face cu mouse-ul se face și cu tastatura, inclusiv mutarea cardurilor pe kanban.
5. **Culoarea nu e singurul semnal.** Prioritatea, starea și erorile au și iconiță sau text.

---

## 2. Tokens de culoare

### 2.1 Paleta (light)

| Token | Hex | Sursă în screenshot | Folosire |
|---|---|---|---|
| `--kb-navy-900` | `#052063` | titlu „Platformă Web”, h1 „Panou principal” | titluri, text pe butoanele bezel, text puternic |
| `--kb-brand-800` | `#122d94` | logo SDG | logo, navbar brand, grafice (serie principală) |
| `--kb-accent-600` | `#135bf7` | clopoțel + badge notificări | link-uri, element activ în sidebar, focus ring, iconițe interactive, badge-uri |
| `--kb-sky-300` | `#91c4fb` | butoanele „Încarcă fișier”, „Actualizare…” | selecție pe kanban, highlight rând, chip „Owner”; nu e fundal de buton: butonul default e `--kb-fill-accent` (§3.3) |
| `--kb-tint-100` | `#ecf4fc` | fundal element activ sidebar | hover/activ în liste și meniuri, fundal chip neutru |
| `--kb-surface-50` | `#f5f8ff` | fundalul paginii | fundal aplicație |
| `--kb-surface-0` | `#ffffff` | carduri, navbar, sidebar | carduri, dialoguri, tabele, câmpuri |
| `--kb-info-bg` | `#c9f2fb` | bannerele cyan | banner informativ |
| `--kb-info-text` | `#39727f` | textul din bannere | text în banner informativ |
| `--kb-success-600` | `#1c7a26` | „+” verde din sidebar (`#1f8229`), închis puțin ca să treacă AA pe `success-bg` | stare „done”, confirmări, chips succes |
| `--kb-warning-600` | `#b54708` | — (adăugat) | prioritate High, avertismente, deadline apropiat |
| `--kb-danger-600` | `#b42318` | — (adăugat) | prioritate Critical, erori, deadline depășit, acțiuni distructive |
| `--kb-text-900` | `#052063` | = navy | titluri |
| `--kb-text-700` | `#394561` | text sidebar | text principal (body) |
| `--kb-text-600` | `#616971` | etichetele „Nume utilizator:” | text secundar, etichete |
| `--kb-text-500` | `#787f86` | valorile gri | placeholder, text dezactivat, meta (doar ≥ 14px) |
| `--kb-border-300` | `#c6c9d0` | linia hr | borduri câmpuri, separatoare puternice |
| `--kb-border-200` | `#e5e8ee` | — (derivat) | borduri carduri, linii tabel |
| `--kb-archived-400` | `#9da9b8` | iconițe sidebar inactive | iconițe inactive, task arhivat |

Fundaluri de stare (derivate, pentru chips/bannere): `--kb-success-bg #e6f4e8`, `--kb-warning-bg #fdf0e6`, `--kb-danger-bg #fbe9e7`.

Fundaluri pline sub text alb: `--kb-fill-accent #135bf7` și `--kb-fill-danger #b42318`, pentru butoanele default și distructiv, hover în meniu, rând selectat, chip de filtru activ, badge și checkbox bifat. `--kb-fill-shade #052063` le închide la hover, la apăsare și pe bordură. Sunt aceleași în light și dark. Textul alb nu stă niciodată direct pe `accent-600` sau `danger-600`, care devin deschise în dark.

### 2.2 Perechi verificate WCAG (contrast)

| Text | Fundal | Raport | Verdict |
|---|---|---|---|
| navy `#052063` | alb | 15.1 | AAA |
| navy `#052063` | sky-300 `#91c4fb` (selecție, highlight, chip „Owner”) | 8.3 | AAA |
| alb | sky-300 `#91c4fb` | 1.8 | ✗ interzis |
| accent `#135bf7` | alb | 5.4 | AA — ok pentru link-uri și iconițe |
| accent `#135bf7` | tint-100 `#ecf4fc` | 4.9 | AA — element activ în sidebar |
| alb | fill-accent `#135bf7` | 5.4 (4.8 în vârful gradientului) | AA — badge-uri, buton default plin, în light și dark |
| alb | fill-danger `#b42318` | 6.6 (5.8 în vârful gradientului) | AA — buton distructiv plin, în light și dark |
| text-700 `#394561` | alb | 9.6 | AAA |
| text-600 `#616971` | alb | 5.6 | AA |
| text-500 `#787f86` | alb | 4.1 | doar text ≥ 14px bold sau ≥ 18px, sau non-text |
| info-text `#39727f` | info-bg `#c9f2fb` | 4.5 | AA (la limită: nu folosi font < 14px în bannere) |
| success `#1c7a26` / danger `#b42318` / warning `#b54708` | alb | 5.4 / 6.6 / 5.4 | AA |
| success `#1c7a26` / danger `#b42318` / warning `#b54708` | fundalul de stare al fiecăreia | 4.8 / 5.6 / 4.9 | AA |

Albastrul deschis al link-urilor din aplicația veche (`#4a8ee6`, 3.3:1) **nu** e preluat — pică AA.

### 2.3 Dark mode

Aceiași tokens semantici, valori diferite. Nu inversăm pur și simplu; păstrăm albastrul ca accent și dăm fundaluri navy-gri.

| Token | Dark |
|---|---|
| `--kb-surface-50` (fundal app) | `#0f1629` |
| `--kb-surface-0` (carduri) | `#161f36` |
| `--kb-tint-100` (hover/activ) | `#1e2a4a` |
| `--kb-border-200` / `-300` | `#273252` / `#344166` |
| `--kb-text-900` | `#eef2ff` |
| `--kb-text-700` | `#cdd5e8` |
| `--kb-text-600` | `#9aa6c2` |
| `--kb-text-500` | `#6f7b96` |
| `--kb-accent-600` | `#5b93ff` (mai deschis, 5.5:1 pe `#161f36`) |
| `--kb-fill-accent` / `--kb-fill-danger` / `--kb-fill-shade` | la fel ca în light: `#135bf7` / `#b42318` / `#052063` (textul alb ar avea doar 3.0:1 pe accentul din dark și 2.8:1 pe danger) |
| `--kb-sky-300` (selecție, highlight) | `#3d6fc9` cu text `#eef2ff` |
| `--kb-info-bg` / `--kb-info-text` | `#113a45` / `#9fdcea` |
| success / warning / danger | `#4ade80` / `#fbbf24` / `#f87171` pe fundaluri `#0f2a16` / `#2d1f05` / `#2d0f0d` |
| `--kb-archived-400` | `#55617d` |
| `--kb-ink` | la fel ca în light, `#052063`: fundalul tooltip-urilor și al snackbar-ului, care au text alb în ambele teme (`navy-900` nu se poate folosi acolo, devine aproape alb în dark) |

Activare: `@media (prefers-color-scheme: dark)` + `html[data-theme="dark"]`; toggle în meniul utilizatorului, memorat în `localStorage` (`kb.theme`). Material primește `color-scheme: light dark` și își schimbă tokens-urile sincron.

### 2.4 Fișierul de tokens

`src/styles/_tokens.scss`:

```scss
:root {
  --kb-navy-900: #052063;
  --kb-brand-800: #122d94;
  --kb-accent-600: #135bf7;
  --kb-sky-300: #91c4fb;
  --kb-tint-100: #ecf4fc;
  --kb-surface-50: #f5f8ff;
  --kb-surface-0: #ffffff;
  --kb-info-bg: #c9f2fb;
  --kb-info-text: #39727f;
  --kb-success-600: #1c7a26;
  --kb-success-bg: #e6f4e8;
  --kb-warning-600: #b54708;
  --kb-warning-bg: #fdf0e6;
  --kb-danger-600: #b42318;
  --kb-danger-bg: #fbe9e7;
  --kb-text-900: #052063;
  --kb-text-700: #394561;
  --kb-text-600: #616971;
  --kb-text-500: #787f86;
  --kb-border-300: #c6c9d0;
  --kb-border-200: #e5e8ee;
  --kb-archived-400: #9da9b8;

  /* Fundaluri pline sub text alb. Rămân la fel în dark, unde accent-600 și danger-600 devin deschise. */
  --kb-fill-accent: #135bf7;
  --kb-fill-danger: #b42318;
  --kb-fill-shade: #052063; /* le închide la hover, la apăsare și pe bordură */
  --kb-ink: #052063; /* fundal închis sub text alb în ambele teme: tooltip, snackbar */

  /* Focus: halo mac-like, 4px, semitransparent */
  --kb-focus-ring: 0 0 0 4px color-mix(in srgb, var(--kb-accent-600) 30%, transparent);

  /* Umbre stratificate (ambient + contact), ca ferestrele/popover-urile mac */
  --kb-shadow-bezel: 0 0.5px 1px rgba(5, 32, 99, 0.10), 0 1px 2px rgba(5, 32, 99, 0.06);
  --kb-shadow-1: 0 1px 2px rgba(5, 32, 99, 0.06), 0 2px 6px rgba(5, 32, 99, 0.05);
  --kb-shadow-2: 0 2px 6px rgba(5, 32, 99, 0.08), 0 8px 24px rgba(5, 32, 99, 0.10);
  --kb-shadow-popover: 0 0 0 0.5px rgba(5, 32, 99, 0.12), 0 4px 10px rgba(5, 32, 99, 0.10), 0 16px 40px rgba(5, 32, 99, 0.14);
  --kb-shadow-sheet: 0 0 0 0.5px rgba(5, 32, 99, 0.12), 0 12px 32px rgba(5, 32, 99, 0.18), 0 32px 80px rgba(5, 32, 99, 0.16);
  --kb-shadow-drag: 0 12px 28px rgba(5, 32, 99, 0.22), 0 2px 6px rgba(5, 32, 99, 0.12);

  /* Highlight interior pe controale (linia de 1px mai deschisă de sus, ca bezel-ul mac) */
  --kb-inner-highlight: inset 0 1px 0 rgba(255, 255, 255, 0.6);
  --kb-inner-highlight-on-accent: inset 0 1px 0 rgba(255, 255, 255, 0.22);

  /* Materiale translucide (sidebar, navbar, meniuri) */
  --kb-glass-bg: rgba(255, 255, 255, 0.72);
  --kb-glass-blur: saturate(180%) blur(20px);

  --kb-radius-xs: 4px;    /* checkbox, badge-uri mici */
  --kb-radius-sm: 6px;    /* butoane, câmpuri, selecție în sidebar */
  --kb-radius-md: 10px;   /* carduri, grupuri de formular */
  --kb-radius-lg: 12px;   /* dialoguri, sheets, popovers, meniuri */
  --kb-radius-pill: 999px;

  --kb-space-1: 4px; --kb-space-2: 8px; --kb-space-3: 12px; --kb-space-4: 16px;
  --kb-space-6: 24px; --kb-space-8: 32px; --kb-space-12: 48px;

  --kb-navbar-h: 52px;
  --kb-sidebar-w: 240px;
  --kb-sidebar-w-collapsed: 56px;
  --kb-content-max: 1440px;

  --kb-font: 'Inter Variable', Inter, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  --kb-font-mono: ui-monospace, 'Cascadia Code', Consolas, monospace;

  --kb-dur-fast: 120ms; --kb-dur-base: 200ms; --kb-dur-slow: 320ms;
  --kb-ease: cubic-bezier(0.2, 0, 0, 1);
}

@mixin dark {
  --kb-surface-50: #0f1629;  --kb-surface-0: #161f36;  --kb-tint-100: #1e2a4a;
  --kb-border-200: #273252;  --kb-border-300: #344166;
  --kb-text-900: #eef2ff;    --kb-text-700: #cdd5e8;   --kb-text-600: #9aa6c2;  --kb-text-500: #6f7b96;
  --kb-accent-600: #5b93ff;  --kb-sky-300: #3d6fc9;    --kb-navy-900: #eef2ff;
  --kb-info-bg: #113a45;     --kb-info-text: #9fdcea;
  --kb-success-600: #4ade80; --kb-success-bg: #0f2a16;
  --kb-warning-600: #fbbf24; --kb-warning-bg: #2d1f05;
  --kb-danger-600: #f87171;  --kb-danger-bg: #2d0f0d;
  --kb-archived-400: #55617d;
  --kb-shadow-bezel: 0 0.5px 1px rgba(0,0,0,.5), 0 1px 2px rgba(0,0,0,.3);
  --kb-shadow-1: 0 1px 2px rgba(0,0,0,.4), 0 2px 6px rgba(0,0,0,.3);
  --kb-shadow-2: 0 2px 6px rgba(0,0,0,.4), 0 8px 24px rgba(0,0,0,.5);
  --kb-shadow-popover: 0 0 0 0.5px rgba(255,255,255,.12), 0 4px 10px rgba(0,0,0,.4), 0 16px 40px rgba(0,0,0,.5);
  --kb-shadow-sheet: 0 0 0 0.5px rgba(255,255,255,.14), 0 12px 32px rgba(0,0,0,.5), 0 32px 80px rgba(0,0,0,.5);
  --kb-inner-highlight: inset 0 1px 0 rgba(255,255,255,.08);
  --kb-glass-bg: rgba(22, 31, 54, 0.72);
}
@media (prefers-color-scheme: dark) { :root:not([data-theme='light']) { @include dark; } }
:root[data-theme='dark'] { @include dark; }
```

Regula: **componentele folosesc doar `var(--kb-*)`**, niciodată hex direct. Material își ia culorile din temă (§3); restul UI-ului din tokens.

---

## 3. Tema Angular Material 22 (M3)

### 3.1 Generare paletă

O singură dată, din `frontend/`:

```sh
ng generate @angular/material:theme-color \
  --primaryColor=#122d94 --secondaryColor=#135bf7 --tertiaryColor=#1f8229 \
  --neutralColor=#616971 --errorColor=#b42318 \
  --directory=src/styles/theme
```

Produce `src/styles/theme/_theme-colors.scss` cu `$primary-palette`, `$tertiary-palette`. Fișierul e generat și **nu se editează manual**; nuanțele exacte de brand se forțează prin overrides.

### 3.2 `src/styles.scss`

```scss
@use '@angular/material' as mat;
@use './styles/theme/theme-colors' as kb;
@use './styles/tokens';
@use '@fontsource-variable/inter';
@use 'material-symbols/outlined.css';

html {
  color-scheme: light dark;
  @include mat.theme((
    color: (primary: kb.$primary-palette, tertiary: kb.$tertiary-palette, theme-type: color-scheme),
    typography: (plain-family: var(--kb-font), brand-family: var(--kb-font), bold-weight: 600, medium-weight: 500, regular-weight: 400),
    density: -1,
  ));

  // Aliniem system tokens Material la brand (light; dark-ul vine din paleta generată)
  @include mat.theme-overrides((
    surface: var(--kb-surface-0),
    surface-container-lowest: var(--kb-surface-0),
    surface-container-low: var(--kb-surface-50),
    surface-container: var(--kb-tint-100),
    outline-variant: var(--kb-border-200),
    outline: var(--kb-border-300),
    on-surface: var(--kb-text-700),
    on-surface-variant: var(--kb-text-600),
    primary: var(--kb-accent-600),
    primary-container: var(--kb-sky-300),
    on-primary-container: var(--kb-navy-900),
    corner-small: var(--kb-radius-sm),
    corner-medium: var(--kb-radius-md),
    corner-large: var(--kb-radius-md),
  ));

  // Butoane mac-like: "filled" = default (fill-accent + alb), "outlined" = bezel (alb + bordură + umbră)
  @include mat.button-overrides((
    filled-container-color: var(--kb-fill-accent),
    filled-label-text-color: #ffffff,
    filled-container-shape: var(--kb-radius-sm),
    filled-container-height: 32px,
    outlined-container-shape: var(--kb-radius-sm),
    outlined-container-height: 32px,
    outlined-outline-color: var(--kb-border-300),
    outlined-label-text-color: var(--kb-text-900),
    text-container-shape: var(--kb-radius-sm),
    text-label-text-color: var(--kb-accent-600),
  ));
  @include mat.card-overrides((elevated-container-color: var(--kb-surface-0), elevated-container-shape: var(--kb-radius-md), outlined-outline-color: var(--kb-border-200), outlined-container-shape: var(--kb-radius-md)));
  @include mat.dialog-overrides((container-shape: var(--kb-radius-lg), container-color: var(--kb-surface-0)));
  @include mat.menu-overrides((container-shape: var(--kb-radius-lg), container-color: var(--kb-surface-0)));
  @include mat.form-field-overrides((outlined-outline-color: var(--kb-border-300), outlined-focus-outline-color: var(--kb-accent-600), outlined-container-shape: var(--kb-radius-sm), container-text-size: 13px, container-height: 32px));
  @include mat.slide-toggle-overrides((selected-track-color: var(--kb-accent-600), track-shape: var(--kb-radius-pill)));
  @include mat.sidenav-overrides((container-background-color: var(--kb-surface-0), container-divider-color: var(--kb-border-200)));
  @include mat.toolbar-overrides((container-background-color: var(--kb-surface-0), container-text-color: var(--kb-navy-900)));
}

html, body { height: 100%; margin: 0; }
body {
  font-family: var(--kb-font); font-size: 14px; line-height: 1.5;
  color: var(--kb-text-700); background: var(--kb-surface-50);
  -webkit-font-smoothing: antialiased;
}
a { color: var(--kb-accent-600); text-decoration: none; }
a:hover { text-decoration: underline; }
:focus-visible { outline: none; box-shadow: var(--kb-focus-ring); border-radius: var(--kb-radius-sm); }
```

Pachete: `@angular/material`, `@angular/cdk`, `@fontsource-variable/inter`, `material-symbols`. Nimic de la Google Fonts/CDN — aplicația rulează în LAN.

### 3.3 Mapare butoane Material → roluri

| Rol | Directivă Material | Aspect (detalii în §6.0.3) |
|---|---|---|
| Default (o singură dată pe dialog/zonă; răspunde la `Enter`) | `matButton="filled"` | fill-accent cu gradient subtil, text alb, highlight interior |
| Bezel / standard | `matButton="outlined"` + clasă `kb-bezel` | alb, bordură border-300, umbră bezel, text navy |
| Text / inline | `matButton` | text accent-600, fără bordură, hover fundal tint-100 |
| Distructiv | `matButton="filled"` + clasă `kb-danger` | fill-danger cu același tratament ca default; **mereu** după dialog de confirmare |
| Iconiță (toolbar) | `matIconButton` | vizual 28×28 în toolbar, 32×32 în conținut, cu zona de click mereu minim 40×40 prin padding transparent (§9); fundal transparent, hover fundal tint-100, rază 6px |

Nu folosim `matButton="elevated"` sau `"tonal"` — un singur limbaj de butoane.

---

## 4. Tipografie

Inter Variable, self-hosted. Scală (size / line-height / weight):

| Stil | Mărime | Folosire |
|---|---|---|
| `kb-display` | 24 / 32 / 600, navy | titlul paginii (h1), unul per pagină |
| `kb-title` | 18 / 26 / 600, navy | titlu card, titlu dialog (h2) |
| `kb-subtitle` | 15 / 22 / 600, text-700 | titlu secțiune în card, coloană kanban (h3) |
| `kb-body` | 14 / 21 / 400, text-700 | text standard, celule tabel, câmpuri |
| `kb-body-strong` | 14 / 21 / 600 | etichete („Nume utilizator:”), nume task pe card |
| `kb-caption` | 12 / 16 / 400, text-600 | meta (date, autor), breadcrumb, header tabel (uppercase **nu**) |
| `kb-mono` | 13 / 20, mono | ID-uri, coduri de eroare (correlation id) |

Reguli: sentence case peste tot („Actualizare conturi”, nu „Actualizare Conturi”); fără `letter-spacing` pe text normal; numerele în tabele cu `font-variant-numeric: tabular-nums`; truncare cu `text-overflow: ellipsis` + tooltip cu textul complet.

---

## 5. Layout

```
┌──────────────────────────────────────────────────────────────┐
│ toolbar 52px, translucid: [≡] [logo Kanabol]                 [🔔3] [◯ email ▾] │
├──────────┬───────────────────────────────────────────────────┤
│ source   │ page header: h1 + breadcrumb        [acțiune default] │
│ list     │───────────────────────────────────────────────────│
│ 240px    │ conținut (max 1440px, padding 24px)                │
│ glass    │   carduri plate / tabel zebra / board              │
│ items    │                                                    │
│ 28px     │                                                    │
└──────────┴───────────────────────────────────────────────────┘
```

- **Navbar (toolbar)**: vezi §6.0.8. Stânga: buton colapsare sidebar (iconiță `dock_to_left`), logo = lockup din §14 (mark 24px înălțime + wordmark „Kanabol” în brand-800, 17px/700, tracking −0.015em, gap 8px), link spre Panou principal. Dreapta: clopoțel cu badge, meniu utilizator. Căutarea globală (`Ctrl+K`) nu e în v1; board-ul are căutare locală după titlu (§7.4).
- **Sidebar (source list)**: vezi §6.0.7. Grupuri: „Lucru” (Panou principal, Proiectele mele, Task-urile mele, Notificări), „Administrare” (Utilizatori, Roluri, Rapoarte — vizibile doar cu permisiune), jos: versiune (`caption`, text-500). Stare memorată în `localStorage` (`kb.sidebar`).
- **Page header**: h1 `kb-display`, sub el breadcrumb (`caption`, „Acasă › Proiecte › Kanabol v1”, separator `›`, ultimul element text-700 fără link). În dreapta, pe aceeași linie cu h1, butonul default al paginii (ex. „Task nou”) și, opțional, un segmented control (Board / Listă). Padding 24px 24px 16px.
- **Conținut**: fundal surface-50, padding 0 24px 24px, `max-width: var(--kb-content-max)`, centrat. Board-ul kanban ignoră max-width (full-bleed cu scroll orizontal).
- **Card**: vezi §6.0.10. Padding 20px 24px. Titlu `kb-title`, 16px sub el conținutul. Carduri într-o pagină: spațiu 16px între ele.

Corectări față de aplicația veche vizibile în screenshot: butoanele nu mai „plutesc” distribuite pe lățime (stau grupate stânga, 8px între ele); nu mai folosim `<input type="file">` nativ (zonă de drop stilizată); bannerele informative nu mai apar între secțiuni ca separatoare — informația merge în subtitlul secțiunii.

---

## 6. Componente

### 6.0 Limbaj de formă (macOS-like)

Ce luăm din macOS: proporțiile controalelor, colțurile, bordurile fine cu highlight, umbrele stratificate, translucența, sheet-urile și popover-urile. Ce **nu** luăm: fontul SF, culorile sistemului, butoanele „semafor”, iconografia SF Symbols. Rezultatul trebuie să fie recognoscibil ca Kanabol (paleta din §2), dar să se simtă ca o aplicație nativă.

#### 6.0.1 Reguli generale

1. **Colțuri**: 6px pe controale (butoane, câmpuri, selecție în liste), 10px pe carduri și grupuri, 12px pe dialoguri, sheets, popovers, meniuri. Colțurile sunt mereu **concentrice**: un element cu padding `p` în interiorul unui container cu rază `R` are raza `R − p` (minim 4px). Niciodată colț drept pe un element care plutește.
2. **Borduri**: 1px (0.5px pe ecrane HiDPI prin `border-width: 0.5px` unde browserul suportă) în `border-300` pe controale, `border-200` pe carduri și separatoare. Controalele albe au în plus `--kb-inner-highlight` (linie de 1px mai deschisă sus) — asta dă efectul de „bezel” macOS.
3. **Umbre**: stratificate, mereu două–trei layere (contact + ambient), colorate cu navy semitransparent, nu negru. Un element plat (card în pagină) **nu** are umbră; umbra înseamnă „plutește” (meniu, popover, sheet, card tras). Cu cât plutește mai sus, cu atât umbra e mai mare și mai difuză (`shadow-bezel` → `shadow-1` → `shadow-2` → `shadow-popover` → `shadow-sheet`).
4. **Materiale**: navbar, sidebar și meniurile sunt translucide (`--kb-glass-bg` + `backdrop-filter: var(--kb-glass-blur)`), cu fallback opac dacă `backdrop-filter` lipsește. Conținutul (carduri, tabele, dialoguri) e opac.
5. **Gradient subtil doar pe butonul default și pe cel distructiv**: `linear-gradient(180deg, color-mix(in srgb, X 92%, white) 0%, X 100%)` — 8% mai deschis sus, imperceptibil conștient, vizibil ca „volum”. Nicăieri altundeva gradienturi.
6. **Mărimi**: controalele au 3 mărimi — `small` 24px (în tabele, chips de filtru), `regular` 28px (toolbar, formulare dense), `large` 32px (butoanele principale ale unui formular/dialog). Default-ul în aplicație e **regular 28px**; Material e configurat pe 32px (`large`) și reducem cu clasa `kb-sm`/`kb-regular`.
7. **Text în controale**: 13px/500 la `regular` și `large`, 12px/500 la `small`. Etichetele de butoane sunt scurte (1–3 cuvinte), cu verb.
8. **Hover e discret, press e vizibil**: hover = fundal cu 4–6% navy peste; press (`:active`) = fundal cu 10% navy peste + `transform: translateY(0.5px)`; nu animăm `scale` pe butoane.
9. **Focus**: halo semitransparent 4px (`--kb-focus-ring`), colțurile halo-ului urmează colțul controlului. Apare doar la `:focus-visible`.
10. **Dezactivat**: opacitate 0.45 pe tot controlul, fără schimbare de culoare, cursor `default` (nu `not-allowed`).

#### 6.0.2 Clase utilitare de formă (`_utilities.scss`)

```scss
.kb-bezel {
  background: var(--kb-surface-0);
  border: 1px solid var(--kb-border-300);
  border-radius: var(--kb-radius-sm);
  box-shadow: var(--kb-shadow-bezel), var(--kb-inner-highlight);
  color: var(--kb-text-900);
  &:hover { background: color-mix(in srgb, var(--kb-navy-900) 4%, var(--kb-surface-0)); }
  &:active { background: color-mix(in srgb, var(--kb-navy-900) 10%, var(--kb-surface-0)); transform: translateY(0.5px); box-shadow: var(--kb-shadow-bezel); }
}
.kb-default {
  --_base: var(--kb-fill-accent);
  --_c: var(--_base);
  background: linear-gradient(180deg, color-mix(in srgb, var(--_c) 92%, white) 0%, var(--_c) 100%);
  border: 1px solid color-mix(in srgb, var(--_c) 80%, var(--kb-fill-shade));
  border-radius: var(--kb-radius-sm);
  box-shadow: var(--kb-shadow-bezel), var(--kb-inner-highlight-on-accent);
  color: #fff;
  &:hover { --_c: color-mix(in srgb, var(--_base) 90%, var(--kb-fill-shade)); }
  &:active { --_c: color-mix(in srgb, var(--_base) 80%, var(--kb-fill-shade)); transform: translateY(0.5px); }
}
.kb-danger { @extend .kb-default; --_base: var(--kb-fill-danger); }
.kb-glass {
  background: var(--kb-glass-bg);
  -webkit-backdrop-filter: var(--kb-glass-blur); backdrop-filter: var(--kb-glass-blur);
  @supports not (backdrop-filter: blur(1px)) { background: var(--kb-surface-0); }
}
.kb-floating { border-radius: var(--kb-radius-lg); box-shadow: var(--kb-shadow-popover); background: var(--kb-surface-0); }
```

Material primește aceste stiluri prin overrides (§3.2) și, acolo unde tokens-urile Material nu ajung (gradient, highlight interior), prin clasele de mai sus aplicate pe `matButton`.

#### 6.0.3 Butoane

```
┌─────────────────────┐   ┌─────────────────────┐   ┌───────────┐
│    Salvează         │   │    Anulează         │   │  Șterge   │
│  (default, accent)  │   │  (bezel, alb)       │   │  (text)   │
└─────────────────────┘   └─────────────────────┘   └───────────┘
   gradient + highlight      bordură + umbră 1px       fără bordură
```

- **Înălțimi**: 24 / 28 / 32. **Padding orizontal**: 10 / 14 / 18px. **Lățime minimă**: 64 / 72 / 88px, ca butoanele dintr-un rând să arate uniform (butoanele pereche „Anulează/Salvează” au aceeași lățime: `min-width` = lățimea celui mai lat).
- **Iconiță în buton**: 16px la `small`/`regular`, 18px la `large`, la stânga textului, gap 6px. Iconiță singură → buton pătrat.
- **Grupare**: butoanele de acțiune ale unui dialog/formular stau **jos-dreapta**, ordinea mac: `[distructiv/terțiar la stânga] ……… [Anulează] [Default]`. Gap 8px. Default-ul e ultimul și răspunde la `Enter`; `Esc` = Anulează.
- **Un singur default pe suprafață** (dialog, card de formular, page header). Dacă ai nevoie de două acțiuni „importante”, a doua e bezel.
- **Buton de meniu** (dropdown): bezel + chevron 12px `expand_more` la dreapta, padding dreapta 8px. Deschis → chevron rotit 180°, fundal ca la `:active`.
- **Segmented control** (înlocuiește tabs mici și toggle-uri de vizualizare, ex. Board/Listă): container pill-ish cu raza 7px, fundal `color-mix(navy 6%, surface-0)`, padding 2px; segmentul activ e alb cu `shadow-bezel` și raza 5px; înălțime 28px; text 13px/500; tranziția segmentului activ 200ms.
- **Switch** (`mat-slide-toggle`): track 26×16, pill, accent-600 când e pornit, knob alb 12px cu `shadow-bezel`. Folosit pentru setări on/off; **nu** pentru acțiuni.
- **Checkbox / radio**: 16px, raza 4px (checkbox) / cerc, bordură border-300, bifat = fundal fill-accent cu bifă albă și același gradient ca default-ul.

#### 6.0.4 Câmpuri de formular

- Înălțime 28px (regular), text 13px, padding 0 8px, fundal alb, bordură border-300, raza 6px, `inner-highlight` inversat (`inset 0 1px 1px rgba(5,32,99,.06)` — câmpurile sunt ușor „scobite”, butoanele ușor „bombate”).
- Focus: bordura devine accent-600 + halo.
- **Etichete la stânga, aliniate dreapta**, pe aceeași linie cu câmpul (stil formular mac/Settings): coloana de etichete 160px, text-600, 13px, urmată de `:`. Sub 768px (§10) etichetele trec deasupra câmpului. Material `mat-form-field` cu etichetă flotantă se folosește doar în dialogurile de creare rapidă (un singur câmp); formularele reale folosesc layout-ul de mai sus cu `matInput` fără `mat-form-field` (clasa `kb-field`).
- **Grupuri de formular** („inset grouped”): câmpurile înrudite stau într-un container alb cu raza 10px, bordură border-200, separatoare border-200 între rânduri (rânduri de 36px), ca în Settings pe mac. Titlul grupului deasupra containerului, `caption` 600 text-600.
- Select: bezel cu chevron dublu (`unfold_more`) la dreapta; lista deschisă e un popover (§6.0.6).
- Search: pill (raza 999px), iconiță `search` 16px la stânga, buton `cancel` la dreapta când are text, înălțime 28px, fundal `color-mix(navy 5%, surface-0)`, fără bordură, bordură accent la focus.
- Text de ajutor: `caption` text-500 sub grup, nu sub fiecare câmp. Eroare: bordura câmpului danger-600 + text danger sub câmp, 12px.

#### 6.0.5 Dialoguri = sheets

Dialogurile modale sunt **sheets**: nu au bară de titlu separată, nu au „×”, se atașează vizual de partea de sus a ferestrei.

- Lățimi: 440 (confirmare), 600 (formular), 920 (detalii task). Raza 12px, fundal alb, `shadow-sheet`, fără bordură (umbra are inel de 0.5px).
- Poziție: ancorate la **12% de sus** (nu centrate vertical), ca să nu sară când conținutul își schimbă înălțimea.
- Animație intrare: 240ms `--kb-ease`, `translateY(-16px) → 0` + opacitate; ieșire 160ms invers. Backdrop `rgba(5,32,99,.28)` cu `backdrop-filter: blur(2px)`.
- Structură: padding 24px; titlu `kb-title` + (opțional) o propoziție `body` text-600 sub el; conținut; rând de butoane jos-dreapta separat prin 24px spațiu (fără linie). Confirmările au iconiță 40px la stânga titlului (cerc tint-100 cu iconiță accent sau cerc danger-bg cu iconiță danger).
- `Esc` închide (= Anulează), `Enter` activează default-ul **doar** dacă focusul nu e într-un textarea. Click pe backdrop nu închide formularele cu modificări; deschide confirmarea „Renunți la modificări?”.
- Dialogul de detalii task nu e sheet, e **panou**: slide din dreapta (`translateX(24px) → 0`), ocupă înălțimea ferestrei minus 24px margine, raza 12px, aceeași umbră; board-ul rămâne vizibil în spate, estompat.

#### 6.0.6 Popovers și meniuri

- Meniuri (`mat-menu`), select-uri deschise, pickeri de dată, panoul de notificări: `kb-floating` + `kb-glass` (translucid), raza 12px, padding 6px, `shadow-popover`.
- Item de meniu: 28px înălțime, raza 6px, padding 0 10px, iconiță 16px + text 13px; hover = fundal fill-accent cu text alb (comportamentul mac), nu tint. Separator: 1px border-200 cu margine 6px vertical. Item distructiv: text danger-600, pe hover fundal fill-danger cu text alb.
- Popover-urile ancorate la un buton (ex. filtre pe board, picker de asignat) au **săgeată** 12px spre elementul-sursă, aceeași culoare cu fundalul, decupată cu `clip-path`. Meniurile din `⋮` nu au săgeată.
- Animație: 160ms, `scale(0.96) → 1` din punctul de ancorare (`transform-origin`), opacitate.
- Tooltip: fundal `--kb-ink` la 90% (nu navy-900, care devine deschis în dark), text alb 12px, raza 6px, padding 4px 8px, fără săgeată, apare la 500ms.

#### 6.0.7 Sidebar = source list

Sidebar-ul urmează „source list”-ul din Finder/Mail: translucid (`kb-glass`), fără bordură dreapta vizibilă (doar o linie `border-200` la 50% opacitate), grupuri cu titlu `caption` 600 text-500 (ex. „Lucru”, „Administrare”) și items de 28px cu raza 6px, margine 2px 10px, iconiță 16px + text 13px/500. Selecția e un **pill plin**: fundal tint-100 cu text și iconiță accent-600; când fereastra nu e focusată (opțional, `:has(:focus-within)` pe `body`) selecția devine gri `color-mix(navy 8%, surface-0)`. Fără bară de 3px la stânga (era în v1.0; scos). Colapsat: 56px, iconițe 20px centrate, tooltip la dreapta.

#### 6.0.8 Navbar = toolbar

Navbar-ul e o bară de unelte: translucidă, 52px (nu 64; mac-ul e mai scund), fără umbră, linie `border-200` jos. Butoanele din ea sunt iconițe 28×28 cu raza 6px fără bordură și zonă de click 40×40 invizibilă (§9); hover fundal `color-mix(navy 6%, transparent)`. Meniul utilizatorului: avatar 24px (inițiale pe tint-100) + email 13px + chevron, într-un buton bezel fără bordură la repaus (bordura apare la hover). Selectorul de an din aplicația veche nu există în Kanabol.

#### 6.0.9 Tabele și liste

- Rânduri de 32px (dense) sau 40px (cu avatar), text 13px, **zebra** subtilă: rândurile pare au fundal `color-mix(navy 3%, surface-0)`. Fără linii orizontale între rânduri (zebra le înlocuiește); o singură linie `border-200` sub header.
- Header 28px, `caption` 600 text-600, fundal surface-0 lipicios (`position: sticky`).
- Selecție rând: fundal fill-accent cu text alb la click (stil Finder) pentru tabelele cu selecție; hover doar `color-mix(navy 5%)`. Pentru tabelele fără selecție (rapoarte) nu există stare de selecție.
- Colțurile tabelului urmează cardul (10px) cu `overflow: hidden`.

#### 6.0.10 Carduri și grupuri

Cardurile din pagină sunt plate: alb, bordură 1px border-200, raza 10px, fără umbră. Cardurile **clicabile** (proiecte, task-uri pe board) au la hover `shadow-1` + bordură border-300, tranziție 120ms. Cardul tras pe kanban: `shadow-drag`, rotație 1.5°, scale 1.02 (singurul `scale` admis).

#### 6.0.11 Chips, badge-uri, toggles de filtru

Chips: 22px înălțime, raza pill, 12px/500, padding 0 8px, fundal de stare, **fără bordură**. Chips de filtru pe board: bezel pill (alb, bordură) 26px; activ = fundal fill-accent text alb. Badge pe clopoțel: 16px minim, pill, fill-accent cu text alb 10px/700, bordură 2px de culoarea navbar-ului ca să „stea” pe iconiță.

#### 6.0.12 Scroll, separatoare, iconițe

- Scrollbars: overlay subțiri, 8px, thumb `color-mix(navy 25%, transparent)` raza pill, apar la hover pe container (`scrollbar-width: thin; scrollbar-color`).
- Separatoare: 1px border-200, niciodată mai groase; secțiunile se separă prin **spațiu** (24px), nu prin linii, cu excepția listelor.
- Iconițele rămân Material Symbols (nu SF Symbols), dar la 16px în controale și în sidebar-ul deschis, 20px în liste și în sidebar-ul colapsat (§6.0.7), 48px doar în empty states (§6.8).

### 6.1 Banner / Alert (inline)
Fundal + text de stare, bordură 1px în culoarea stării la 30% + rază 10px, padding 12px 16px, iconiță 20px la stânga, opțional buton „Închide”. Variante: info (info-bg/info-text, iconiță `info`), success, warning, danger. Folosit pentru context persistent (ex. „Acest proiect e arhivat”), **nu** pentru feedback la acțiuni — pentru asta e snackbar-ul.

### 6.2 Snackbar
`MatSnackBar`, jos-stânga, 4 s (8 s pentru erori), fundal `--kb-ink` la 92% cu `kb-glass` (nu navy-900, care devine deschis în dark), text alb, rază 10px, `shadow-popover`, acțiune în sky-300. Eroare: prefix iconiță `error` danger și textul „Cod: `a1b2c3`” (ultimele 6 caractere din correlation id, mono) + acțiune „Copiază” (copiază id-ul complet). O singură snackbar vizibilă; următoarea o înlocuiește.

### 6.3 Formulare
Formă și layout în §6.0.4 (etichete la stânga aliniate dreapta, grupuri inset, câmpuri 28px). Grid: un grup de formular ocupă 100% până la 640px lățime; la formulare late, două grupuri unul lângă altul (gap 24px) la ≥ 960px. Eroarea apare la `blur` sau la submit. Butoanele formularului: jos-dreapta, „Anulează” (bezel) + „Salvează” (default); `Enter` trimite (în afara textarea), `Esc` anulează în dialog. Câmpuri obligatorii marcate cu `*` după etichetă; nu marcăm opționalele.

Upload: zonă de drop 100% × 120px, bordură 1px dashed border-300, rază 10px, fundal `color-mix(navy 3%, surface-0)`, iconiță `upload` 20px + „Trage fișiere aici sau **alege**”; la dragover bordura devine accent-600 solidă și fundal tint-100. Sub ea, lista fișierelor ca rânduri de 36px într-un grup inset, cu progres (bară 3px pill accent-600) și buton `close` 16px.

### 6.4 Tabele
Formă în §6.0.9 (zebra, 32/40px, header sticky, selecție accent). `mat-table` cu `kb-table`. Coloane numerice aliniate dreapta, `tabular-nums`. Acțiuni pe rând într-un meniu `⋮` la capăt (buton iconiță 32×32 vizibil la hover pe rând, mărimea admisă în rânduri dense de §9), apar doar cu permisiune. Paginare `mat-paginator` 25/50/100 ca bară de 36px sub tabel, sortare pe header cu chevron 12px. Pe < 768px tabelele devin liste de carduri (fiecare rând un card cu 2–3 câmpuri cheie).

### 6.5 Chips / badge-uri
Formă în §6.0.11 (22px, pill, fără bordură). Opțional punct 6px la stânga.

| Prioritate | Chip |
|---|---|
| Low | fundal `#eef0f3`, text text-600, iconiță `arrow_downward` |
| Medium | fundal tint-100, text accent-600, iconiță `remove` |
| High | warning-bg / warning-600, iconiță `arrow_upward` |
| Critical | danger-bg / danger-600, iconiță `priority_high` |

Alte chips: „Owner” (navy-900 pe sky-300), „Membru” (text-700 pe `#eef0f3`), „Arhivat” (archived-400 pe `#eef0f3`), deadline: verde (> 3 zile), warning (≤ 3 zile), danger (depășit; iconiță `event_busy`).

### 6.6 Dialoguri
Formă în §6.0.5 (sheets ancorate sus, fără „×”, 440/600/920). `MatDialog` cu `panelClass: 'kb-sheet'` și `position: { top: '12vh' }`. Confirmare distructivă: titlul spune ce se întâmplă („Arhivezi task-ul «Login SSO»?”), o propoziție explică consecința, butonul distructiv e `kb-danger` și are verbul („Arhivează”), niciodată „OK/Da”; iconiță `archive` în cerc danger-bg la stânga titlului.

### 6.7 Meniuri, tooltips, tabs
Formă în §6.0.6 (popover translucid, item 28px, hover fill-accent cu text alb). Tabs: pentru 2–4 vederi ale aceleiași entități (Board / Listă / Membri / Setări) folosim **segmented control** (§6.0.3) în page header, nu `mat-tab-group`; `mat-tab-group` rămâne doar în dialogul de detalii task (Comentarii / Atașamente / Istoric), cu indicator 2px accent-600, etichete 13px/500, înălțime 36px, fără uppercase.

### 6.8 Stări de pagină
- **Loading**: skeleton (blocuri `#e9edf5` cu shimmer 1.2 s), aceeași formă ca și conținutul final; niciodată spinner pe pagină întreagă. Spinner 20px doar în butoane (înlocuiește eticheta, butonul rămâne de aceeași lățime).
- **Empty**: ilustrație simplă (iconiță 48px archived-400 într-un cerc tint-100), titlu `kb-subtitle` („Niciun task încă”), o propoziție, acțiunea primară („Creează primul task”) dacă utilizatorul are permisiunea.
- **Error**: iconiță `error` danger, „Nu am putut încărca…”, buton „Reîncearcă”, caption cu codul de eroare.
- **Fără permisiune**: elementul nu se afișează deloc (`*kbHasPermission`); nu afișăm butoane dezactivate cu „nu ai voie”. Excepție: pagini accesate prin URL direct → pagina 403 cu „Nu ai acces la această pagină” + link spre Panou principal.

---

## 7. Ecrane

### 7.1 Login
Pagină fără shell: fundal surface-50, card 400px centrat, logo + „Kanabol” sus, câmpuri Email și Parolă (cu buton „arată”), buton filled 100% „Intră în cont”, eroare generică „Email sau parolă incorecte” (nu spunem care). Fără „Ține-mă minte” (refresh token-ul face asta). Pagina de schimbare obligatorie a parolei are aceeași structură + regulile parolei listate cu bife live.

### 7.2 Panou principal
Grid 12 coloane, gap 16px. Rând 1: 4 stat tiles (Task-urile mele deschise / Întârziate / Finalizate săptămâna asta / Proiecte active) — card cu caption + număr 28px/600 navy + delta caption. Rând 2: „Task-urile mele” (tabel compact 8 rânduri, col 8) + „Notificări recente” (col 4). Rând 3: „Proiectele mele” (carduri 280px cu nume, owner, progres bară 4px sky-300, număr task-uri).

### 7.3 Proiecte
Listă ca tabel (Nume, Owner, Membri (avatare suprapuse max 4 + „+N”), Task-uri deschise, Întârziate, Actualizat). Click pe rând → board. Buton default „Proiect nou” (sheet 600px). Pagina proiectului are segmented control în header: **Board** / Listă / Membri / Rapoarte / Setări (coloane, arhivare). „Rapoarte” apare pentru ownerii proiectului și pentru cine are `reports.view`, cu filtrul de proiect fixat pe proiectul curent (SPEC §5.1.1); e singura intrare la rapoarte pentru un owner fără permisiune.

### 7.4 Board (kanban)
- Container full-bleed, scroll orizontal, padding 24px, coloane cu gap 16px.
- **Coloană**: lățime 300px, fundal `color-mix(in srgb, var(--kb-navy-900) 4%, var(--kb-surface-50))`, rază 10px, padding 10px. Header: nume `kb-subtitle` + count chip (text-600 pe `#eef0f3`) + meniu `⋮` (redenumește, mută, marchează „done”, șterge dacă e goală). Coloana `is_done` are iconiță `check_circle` success-600 lângă nume. Scroll vertical intern, max-height = viewport − header. Jos: buton text „+ Task” (permisiune `tasks.create` + membru).
- **Card task**: alb, bordură border-200, rază 8px (concentric cu coloana de 10px și padding 10px → 8px e deja generos; minimul din §6.0.1 e 4px), padding 10px 12px, gap 6px vertical. Rând 1: cheie `KAN-123` (mono caption text-500) + chip prioritate dreapta. Rând 2: titlu `body-strong`, max 2 linii. Rând 3: chip deadline (dacă există) + iconițe cu count (`chat_bubble` comentarii, `attach_file` atașamente) + avatar asignat 24px dreapta. Hover: umbră `shadow-1`, bordură border-300. Click: deschide panoul de detalii (§6.0.5, slide din dreapta). Neasignat: avatar gol cu bordură dashed.
- **Drag & drop** (CDK): la pickup cardul capătă `shadow-drag`, rotație 1.5°, scale 1.02; placeholder = contur dashed border-300 pe fundal tint-100 de aceeași înălțime; coloana țintă validă primește bordură 2px accent-600. Animație de reordonare 200 ms. **Tastatură**: card focusat → `Space` ridică, `←/→` schimbă coloana, `↑/↓` poziția, `Space` lasă, `Esc` anulează; anunț `aria-live` („Mutat în «In progress», poziția 2 din 5”).
- Update de la alt utilizator (SignalR): cardul se mută cu animație și pulsează o dată cu fundal tint-100 (600 ms); nu afișăm toast pentru fiecare mutare.
- Filtre deasupra board-ului: chips „Ale mele”, „Întârziate”, asignat (avatar picker), prioritate; căutare locală după titlu. Filtrele active se reflectă în URL (`?assignee=…`).

### 7.5 Detalii task (panou 920px, slide din dreapta)
Două coloane: stânga (620px) — titlu editabil inline (click → input, `Enter` salvează, `Esc` anulează), descriere (editor text simplu cu markdown de bază, buton „Editează”; randarea trece prin sanitizare, HTML brut nu se afișează, SPEC §16), tabs **Comentarii** / Atașamente / Istoric. Dreapta (300px, fundal surface-50) — proprietăți ca listă etichetă/valoare, fiecare editabilă în loc: Stare (coloană), Asignat, Prioritate, Deadline, Creat de / la, Actualizat la. Jos-dreapta: meniu `⋮` cu „Arhivează” (danger). Comentariu: avatar + nume + timp relativ (tooltip cu data completă), text, acțiuni „Editează” doar pe ale tale. Istoric: timeline verticală, punct 8px accent-600, „**Florin** a schimbat *Prioritate*: Medium → High · acum 2 ore”. URL-ul `/tasks/KAN-123` deschide direct dialogul peste board.

### 7.6 Notificări
Panou (`mat-menu` 400px) din clopoțel: listă ultimele 10, necitite cu fundal tint-100 și punct accent; click → task + marcare citit; „Marchează toate ca citite”; link „Vezi toate” → pagină cu listă completă și filtre.

### 7.7 Utilizatori și roluri
Tabele standard. Formular utilizator în sheet 600px, cu etichete la stânga (§6.0.4). Roluri: listă stânga (280px) + detalii dreapta cu permisiunile grupate pe arie (Utilizatori, Proiecte, Task-uri, Rapoarte) ca checkbox-uri; rolul Admin afișat cu lacăt și checkbox-uri dezactivate (singurul caz permis de „dezactivat cu explicație”: tooltip „Rolul de sistem nu se poate modifica”).

### 7.8 Rapoarte
Filtre sus (interval cu presets „Săptămâna asta / Luna asta / Ultimele 30 zile”, proiect, utilizator) într-un card; sub, 3 secțiuni corespunzând metricilor din SPEC §5.1.1: stat tiles + un grafic fiecare. Grafice: linie/bară simple (Chart.js sau ngx-charts), serie principală brand-800, secundară sky-300, grilă border-200, fără 3D, fără gradient, tooltips în stilul snackbar. Tabel cu datele sub fiecare grafic (exportabil CSV).

---

## 8. Motion

| Caz | Durată / easing |
|---|---|
| Hover, focus, chips | 120 ms, `--kb-ease` |
| Meniu, popover, select deschis | 160 ms, `scale(0.96) → 1` din punctul de ancorare + opacitate (§6.0.6) |
| Tooltip, snackbar | 200 ms, fade + translate 4px |
| Sheet (dialog) | 240 ms `--kb-ease`, `translateY(-16px) → 0` + opacitate; închidere 160 ms (§6.0.5) |
| Reordonare kanban, sidebar collapse | 200 ms |
| Pulsare update remote | 600 ms, o singură dată |
| Skeleton shimmer | 1.2 s loop |

`prefers-reduced-motion: reduce` → toate tranzițiile 0 ms, shimmer-ul devine static.

---

## 9. Accesibilitate

- Contrast minim AA pe tot ce e text; vezi §2.2. Focus ring vizibil pe orice element interactiv (`--kb-focus-ring`), nu îl scoatem niciodată.
- Toate iconițele-buton au `aria-label`; iconițele decorative `aria-hidden`.
- Ordine de tab logică: navbar → sidebar → conținut. `Skip to content` link ascuns, vizibil la focus.
- Kanban operabil din tastatură (§7.4) + `aria-live="polite"` pentru rezultat.
- Dialogurile blochează focusul (Material o face) și revin focusul pe elementul declanșator.
- Formulare: erorile legate de câmp prin `aria-describedby`; la submit cu erori, focus pe primul câmp invalid.
- Nu transmitem stare doar prin culoare: prioritatea și deadline-ul au iconiță + text.
- Zona de click minim 40×40 pentru butoane-iconiță, chiar dacă vizual au 28 sau 32px (padding transparent, §3.3); 32×32 admis în rândurile dense de tabel.
- `lang="ro"` pe `<html>`; `prefers-reduced-motion` respectat.

---

## 10. Responsive

| Lățime | Comportament |
|---|---|
| ≥ 1280 | Layout complet, sidebar 240px (§5) |
| 960–1279 | Sidebar colapsat implicit (56px, §6.0.7), tabele complete |
| 768–959 | Sidebar ca drawer (overlay) din butonul ≡; dialogurile 90% lățime; detalii task pe o coloană |
| < 768 | Nu e țintă în v1 (SPEC: fără mobil). Nu trebuie să fie frumos, dar nu trebuie să fie rupt: tabele → carduri, board scroll orizontal cu coloane 280px |

---

## 11. Text și limbă

- Română cu diacritice corecte (ș, ț cu virgulă, nu cedilă), sentence case, fără semne de exclamare.
- Verbe la imperativ pe butoane („Salvează”, „Arhivează”, „Creează task”); etichetele de câmp substantive („Deadline”, „Asignat”).
- Mesaje de eroare: ce s-a întâmplat + ce poate face („Nu am putut salva task-ul. Reîncearcă sau trimite codul `a1b2c3` administratorului.”).
- Date: `dd.MM.yyyy` (`04.11.2026`), cu oră `dd.MM.yyyy HH:mm`; timp relativ („acum 5 min”, „ieri”, „acum 3 zile”) pentru < 7 zile, altfel data; tooltip cu data completă pe orice timp relativ.
- Numere: separator de mii `.`, zecimal `,` (locale `ro-RO`).
- Termeni fixați: *task* (nu „sarcină”), *board*, *proiect*, *deadline*, *asignat*, *owner*, *membru*, *arhivat*, *prioritate*; coloane implicite: „De făcut”, „În lucru”, „Gata”.
- Toate textele prin `i18n` (Angular localize), chiar dacă v1 e doar română.

---

## 12. Iconițe

Material Symbols Outlined, self-hosted, `font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 20`. Mărimi: 16px în controale, butoane și sidebar-ul deschis (18px în butoanele `large`), 20px în liste, bannere și sidebar-ul colapsat, 48px în empty states (§6.0.3, §6.0.7, §6.0.12, §6.8). Activ/selectat: `FILL 1`. Set fix pentru navigație: `dashboard`, `folder`, `task_alt`, `notifications`, `group`, `admin_panel_settings`, `bar_chart`, `settings`, `logout`.

---

## 13. Structură fișiere de stil

```
src/
├── styles.scss                 # temă Material + globale (§3.2)
└── styles/
    ├── _tokens.scss            # §2.4
    ├── _typography.scss        # clasele kb-display … kb-mono
    ├── _utilities.scss         # .kb-bezel, .kb-default, .kb-danger, .kb-glass, .kb-floating (§6.0.2) + .kb-stack, .kb-row, .kb-truncate, .kb-visually-hidden
    └── theme/
        └── _theme-colors.scss  # generat, nu se editează
```

Componentele au `.scss` propriu cu stiluri locale; nu există `::ng-deep`. Overrides Material doar prin mixin-urile `mat.*-overrides` în `styles.scss`.

---

## 14. Identitate: logo

![Logo Kanabol](images/brand-preview.png)

### 14.1 Semnul

**Ganteră cu discuri-carduri** (conceptul #02 din prima rundă, ales pe 2026-11-04). O ganteră orizontală: bară subțire, două discuri interioare și două discuri exterioare; discurile exterioare au două linii scurte, ca un card kanban. Spune „kanban” (cardurile) și „pe steroizi” (gantera) într-o singură formă, fără literă — poate trăi și fără nume, ca iconiță de aplicație.

Construcție pe grila 100×100 (tile) — coordonatele sunt sursa de adevăr, nu desenul:

| Element | Geometrie | Culoare pe tile / pe deschis / pe navy |
|---|---|---|
| Bară | `rect 30,47 40×6 r3` | alb / navy-900 / alb |
| Disc exterior stânga / dreapta | `rect 12,32 11×36 r3` / `rect 77,32 11×36 r3` | sky-300 (toate) |
| Disc interior stânga / dreapta | `rect 24,38 9×24 r3` / `rect 67,38 9×24 r3` | alb / navy-900 / alb |
| Linii „card” (pe discurile exterioare) | `rect 15,38 5×3 r1` + `rect 15,44 5×3 r1`, oglindit la `x=80` | navy-900 (toate) |
| Tile (app icon) | `rect 0,0 100×100 r22`, gradient vertical `#1a3aa8 → #052063` | — |

Mark-ul standalone e decupat la `viewBox 8 28 84 44` (raport ≈ 1.9:1). Proporțiile nu se modifică: bara rămâne 6/36 din înălțimea discului exterior, discul interior 2/3 din cel exterior.

### 14.2 Variante și când se folosesc

| Fișier | Folosire |
|---|---|
| `kanabol-mark.svg` | Mark pe fundal deschis (navbar, documente, email). Navy + sky. |
| `kanabol-mark-on-dark.svg` | Mark pe fundal navy/închis (dark mode, footer, slide-uri). Alb + sky. |
| `kanabol-mark-mono.svg` | O singură culoare (`currentColor`), fără liniile de card. Pentru watermark, print monocrom, iconițe de sistem. |
| `kanabol-lockup.svg` / `-on-dark.svg` | Mark + wordmark, orizontal. Logo-ul „oficial”. Textul e `<text>` cu Inter — înainte de orice export extern se convertește în contururi. |
| `kanabol-app-icon.svg` + `-512.png`, `-192.png`, `apple-touch-icon.png` | Tile cu gradient, pentru PWA manifest, Windows tile, bookmark pe telefon. |
| `kanabol-favicon.svg`, `favicon-32.png`, `favicon-16.png` | Tile **fără liniile de card** (dispar sub 24px). `<link rel="icon" href="/brand/kanabol-favicon.svg">` + PNG fallback. |

### 14.3 Wordmark

„Kanabol”, Inter 700, tracking −0.015em, brand-800 `#122d94` pe deschis / alb pe navy. Înălțimea literelor mari = 70% din înălțimea mark-ului (mark 24px → text 17px; mark 44px → text 34px). Gap între mark și text = 1/3 din înălțimea mark-ului. Nu există wordmark fără mark, cu excepția textului curent (ex. în e-mailuri plain-text).

### 14.4 Reguli

- **Spațiu liber** în jurul logo-ului: minim înălțimea unui disc interior (24/100 din înălțimea tile-ului) pe toate laturile. Nimic nu intră în acest spațiu.
- **Mărimi minime**: mark standalone 20px înălțime; lockup 24px înălțime; sub 24px se folosește favicon-ul (fără linii).
- **Culori**: doar paleta din §2. Pe fundaluri colorate (success, danger) se folosește varianta mono în alb. Niciodată gradient pe mark în afara tile-ului.
- **Nu**: rotit, înclinat, cu umbră, cu contur, cu discurile recolorate, cu bara îngroșată, întins pe o axă, cu alt font la wordmark, cu „Kanabol” scris cu majuscule.
- În aplicație, logo-ul din navbar e link spre Panou principal; are `aria-label="Kanabol — Panou principal"`; mark-ul e inline SVG (nu `<img>`), ca să preia `currentColor` în mono și să nu facă request separat.
- Avatarul implicit al aplicației (ex. notificări de sistem, „Kanabol” ca autor în istoric) e tile-ul la 24px.

### 14.5 Unde stau fișierele

- Vault: `Atasamente/logo/` — sursa, inclusiv `brand-preview.png` și planșele de concepte (`kanabol-logo-concepts.png`, `kanabol-k-growth.png`, `kanabol-k-arrow.png`, `kanabol-k-dumbbell.png`) ca istoric al deciziei.
- Repo: `frontend/public/brand/` — doar fișierele folosite la runtime (mark, mark-on-dark, favicon svg/png, app icon png, apple-touch-icon); `index.html` le referă de acolo, `manifest.webmanifest` folosește `-192` și `-512`.

---

## 15. Checklist de aplicare (pentru implementare)

- [ ] `npm i @angular/material @angular/cdk @fontsource-variable/inter material-symbols` (+ `ng add @angular/material` răspunzând „custom theme”, fără typography globală, animations da)
- [ ] `ng generate @angular/material:theme-color …` (§3.1)
- [ ] `_tokens.scss`, `styles.scss` conform §2.4 / §3.2; `data-theme` toggle în meniul utilizatorului
- [ ] Shell: navbar + sidenav + page header ca în §5, cu stările activ/hover/colapsat
- [ ] Componente shared: `kb-banner`, `kb-chip` (prioritate/deadline/rol), `kb-empty-state`, `kb-skeleton`, `kb-confirm-dialog`, `kb-upload-zone`, `kb-avatar`
- [ ] Snackbar service cu varianta de eroare + cod
- [ ] Board + card task + DnD cu tastatură (§7.4)
- [ ] Verificare contrast cu axe DevTools pe light și dark; `prefers-reduced-motion` testat
- [ ] Logo: copiază fișierele din §14.5 în `frontend/public/brand/`, favicon + manifest în `index.html`, lockup inline SVG în navbar
- [ ] Captură de ecran a fiecărui ecran din §7 în `docs/screens/` pentru comparație la review
