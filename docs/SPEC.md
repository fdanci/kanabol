# SPEC — Kanabol (Kanban pe steroizi)

> Specificație tehnică pentru prima versiune. Produsul și regulile de business sunt în vault-ul Obsidian (`_Overview.md`, `_Architecture.md`, `Decisions/`). Acest fișier stabilește **cum** construim: arhitectură, pattern-uri, convenții, loguri, teste, lint, Docker și pipeline-ul local de commit.
>
> Stare: v1.25 — 2026-11-09 (v1.24 + deciziile grupului 19: cum s-au implementat headerele cu `always` și `inlineCritical: false` în §13.2, limitele, evidența și canalul hub-ului în §9, redactarea în §8.1, curățarea `refresh_tokens` în §4.5 și `Auth__*` în §13.3, bundle-ul 893 → 706 kB în §6.2, timpii merge-ului în §14.6, nota `check-hardening.sh` în §14.7; și deciziile de dinaintea grupului 19b: release-ul pe server fără registry, `demo-data.sh`, detaliile backup-ului și ale verificării UI în §13.5, `check-release.sh` în §14.7. v1.24 — 2026-11-09: v1.23 + deciziile grupului 18: cum s-au implementat endpoint-urile rapoartelor în §5.1.1, fișierele noi în §6.1 și `chart.js` 4.6.1 în §2, nota `check-reports.sh` în §14.7, runtime-ul `@defer` în §6.2, timpul merge-ului în §14.6; și deciziile de dinaintea grupurilor 19 și 19b: headerele și CSP în §13.2 și §16, limitele hub-ului și canalul de trimitere în §9, redactarea în §8.1, curățarea `refresh_tokens` în §4.5, bundle-ul în §6.2, livrarea (backup, release, deploy, verificarea UI) în §13.5, `check-hardening.sh` și `check-release.sh` în §14.7. v1.23 — 2026-11-09: v1.22 + deciziile grupului 17a: cum s-au implementat panoul principal și task-urile mele în §4.4, fișierele noi în §6.1, nota `check-dashboard.sh` în §14.7; și deciziile de dinaintea grupului 18: cele trei endpoint-uri `/api/reports/*` și autorizarea lor în §5.1.1, `reports/` și ruta proiectului în §6.1, `check-reports.sh` în §14.7. v1.22 — 2026-11-08: v1.21 + deciziile grupului 17: cum s-au implementat worker-ul în §4.3, notificările în §4.4, `core/notifications/` în §6.1, `Realtime.open()` în §9, nota `check-alerts.sh` în §14.7; și deciziile de dinaintea grupului 17a: `GET /api/me/summary`, `GET /api/me/tasks`, `mine` și `completedTaskCount` la proiecte în §4.4, `dashboard/` în §6.1, `check-dashboard.sh` în §14.7. v1.21 — 2026-11-08: v1.20 + deciziile grupului 16b: cum s-a implementat intrarea cu textele în §4.4, nota `check-history.sh` 6 în §14.7; și deciziile de dinaintea grupului 17: worker-ul, `AlertsOptions` și regula alertei în §4.3, paragraful „Notificările” în §4.4, clopoțelul cu `import()` dinamic în §9, `Alerts__*` în §13.3, `check-alerts.sh` în §14.7. v1.20 — 2026-11-08: v1.19 + deciziile grupului 16: forma intrării de istoric în §4.4, datele la microsecundă în §5.1, lista de extensii la pornire în §10, nota `check-history.sh` în §14.7; și deciziile de dinaintea grupului 16b: textele descrierii la cerere prin `GET /api/tasks/{id}/history/{entryId}` în §4.4, pachetul `diff` în §2. v1.19 — 2026-11-08: v1.18 + deciziile grupului 15: notificarea doar membrilor activi și comentariul fără istoric în §4.3 și §4.4, vizibilitatea pe task arhivat, `canEdit`/`canDelete` și `GET /api/comments/{id}` în §4.4, XHR în §6.2, regulile upload-ului, download-ul ca blob și `Storage__AllowedExtensions` cu virgule în §10 și §13.3, nota `check-comments.sh` în §14.7; și deciziile de dinaintea grupului 16: paragraful „Istoricul unui task” în §4.4, `check-history.sh` în §14.7. v1.18 — 2026-11-07: v1.17 + deciziile de dinaintea grupului 15: comentariile și atașamentele în §4.4 și §10, mesajul `CommentAdded` în §4.3, `CommentAdded` și `TaskUpdated` cu `Attachments` în §9, `Storage__AllowedExtensions` în §13.3, `check-comments.sh` în §14.7. v1.17 — 2026-11-07: v1.16 + deciziile grupului 14: hub-ul doar peste WebSockets, `LeaveProject`, refuzul cu cod, `X-Connection-Id`, `NotificationCreated`, conexiunea la primul board și limitele cunoscute în §9, token-ul din query string și refresh-ul pentru altcineva în §4.5, notifier-ul în Api în §4.1, pachetul de test în §2, `AccessTokenRedaction` în §8.1, `access_log off` în §13.2, ținta de 180 s în §14.6, `check-realtime.sh` în §14.7. v1.16 — 2026-11-07: v1.15 + deciziile de dinaintea grupului 14 în §9: payload-urile evenimentelor, `TaskUpdated` și din `TaskAssignedEvent`, propria conexiune prin `X-Connection-Id`, recitirea board-ului; `check-realtime.sh` în §14.7. v1.15 — 2026-11-07: v1.14 + deciziile grupului 13b: migrarea `AddTaskAssignees` și `task_assignees` în §5.1, `TaskAssignedEvent` cu `AddedIds` și `RemovedIds` în §4.3, endpoint-ul vechi, validarea listei, sortarea și numele din istoric în §4.4, `xmin`-ul la schimbarea asignaților în §5.3, `check-assignees.sh` în §14.7. v1.14 — 2026-11-07: v1.13 + mai mulți asignați pe un task, decis 2026-11-07, în §4.3, §4.4, §5.1, §5.1.1, §7.2 și §14.7; deciziile grupului 13: excepția creării în §4.2 și §5.3, coloanele și board-ul în §4.4, `check-board.sh` în §14.7. v1.13 — 2026-11-06: v1.12 + deciziile grupului 12: parserul propriu de markdown în §2 și §16, mesajul notificării și istoricul în §4.3 și §5.1, apartenența reală la orice schimbare și endpoint-urile task-urilor în §4.4, numărul de task printr-un `UPDATE … RETURNING` atomic în §5.1 și §5.3, `check-tasks.sh` în §14.7. v1.12 — 2026-11-06: v1.11 + reîncercarea pe server la alocarea numărului de task în §5.1 și §5.3. v1.11 — 2026-11-06: v1.10 + deciziile grupului 11: proiectele, membrii și owner-ul dezactivat în §4.4, `Project` în §5.1, `xmin`-ul proiectului la schimbarea membrilor în §5.3, `check-projects.sh` în §14.7. v1.10 — 2026-11-06: v1.9 + deciziile grupului 10: cine trimite și ce primește `POST /api/client-logs` în §8.3, `check-admin.sh` în §14.7. v1.9 — 2026-11-06: v1.8 + `check-web.sh` în §14.7. v1.8 — 2026-11-06: v1.7 + deciziile grupului 8b: reactivarea și resetarea parolei în §4.4, `MustChangePassword` în verificarea din baza de date, `users.is_active` ca concurrency token în §4.4, §5.1 și §5.3, ținta merge-ului în `main` ridicată la 150 s în §14.6, parola proprie în §16).

---

## 0. Rezumatul deciziilor

| Subiect | Decizie |
|---|---|
| Nume | **Kanabol** — namespace C# `Kanabol`, prefix Angular `kb`, repo `kanabol`, DB `kanabol` |
| Backend | .NET 10 (LTS), ASP.NET Core, **Clean Architecture + CQRS light** (fără MediatR) |
| ORM | EF Core 10, **code first**, PostgreSQL 17 (Npgsql), naming `snake_case` |
| API | Controllers REST + OpenAPI/Swagger, ProblemDetails pentru erori |
| Auth | Utilizatori proprii, JWT access (15 min) + refresh token rotit (7 zile, cookie HttpOnly) |
| Autorizare | Permisiuni (nu roluri) la nivel de aplicație, **un singur rol per utilizator** + rol pe proiect (`Owner`/`Member`) |
| Ștergere task | **Arhivare** (`archived_at` + global query filter), restaurabilă de admin; niciodată hard delete |
| Alerte | **Doar in-app** (notificări + SignalR); fără email în v1 și fără abstracție pregătită pentru el |
| Productivitate | 3 metrici: task-uri finalizate pe perioadă, lead/cycle time, întârziate + rata de respectare a deadline-ului |
| WIP limit | Nu în v1 |
| Frontend | Angular 22, standalone, **Signals + servicii** (fără store extern), Angular Material + CDK |
| Real-time | SignalR din v1, un singur hub cu grupuri per proiect și per utilizator, alimentat de domain events |
| i18n | Cod/DB/API în engleză, UI în română prin `@angular/localize` (sursă `ro`) |
| Loguri | **Serilog** structurat (JSON) → consolă + **Seq** în Docker, `CorrelationId` pe fiecare request |
| Teste back | xUnit + NSubstitute + FluentAssertions + Testcontainers (Postgres real) |
| Teste front | Vitest (builder Angular), `*.spec.ts` lângă fișier |
| Lint back | `.editorconfig` + analizoare .NET (`TreatWarningsAsErrors`), `dotnet format` |
| Lint front | ESLint flat config (angular-eslint + typescript-eslint strict) + Prettier |
| Atașamente | Disc local în volum Docker, în spatele `IFileStorage` |
| Docker | Multi-stage Dockerfiles, `compose.yaml` (postgres, seq, api, web/nginx) |
| Git | Feature branches → `dev` → `main`, Conventional Commits, **Husky + lint-staged**; suita completă de teste rulează la commit/merge pe `main` |
| Deploy | Server Linux, Docker Compose, nginx servește SPA + proxy către API |
| TLS | Certificat **self-signed** (CA proprie), terminat în nginx, importat o dată în browserele colegilor |

---

## 1. Scop și limite

Task manager intern, tip Jira-light: utilizatori + RBAC, proiecte, task-uri cu prioritate, kanban configurabil cu drag & drop, comentarii + atașamente, istoric modificări, alerte deadline, productivitate per dezvoltator/proiect. Fără integrări externe, fără mobil, fără LLM în v1.

Constrângeri care au ghidat deciziile:
- **Un singur dezvoltator**, timp limitat → preferăm convenții clare și puțin ceremonial față de flexibilitate maximă.
- **Intern, o singură instanță** → fără multi-tenancy, fără scalare orizontală în v1 (dar fără decizii care o blochează).
- **Trebuie să convingă** → calitate vizibilă: rapid, fără bug-uri, loguri bune.

---

## 2. Stack și versiuni

| Componentă | Versiune | Motiv |
|---|---|---|
| .NET SDK | 10.0 (LTS, suport până nov. 2028) | LTS curent |
| EF Core + Npgsql | 10.x | pereche cu .NET 10 |
| PostgreSQL | 17 | stabil, suportat de Npgsql 10 |
| Node.js | 22 LTS | cerut de Angular 22 |
| Angular | 22.x | curent (iunie 2026); upgrade la 24 LTS când apare |
| Angular Material + CDK | 22.x | aceeași versiune cu Angular |
| Seq | ultimul `datalust/seq` | gratuit pentru un singur utilizator |

Pachete NuGet cheie: `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `FluentValidation`, `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Swashbuckle.AspNetCore`, `Scrutor` (decoratori), `Testcontainers.PostgreSql`, `Respawn`, `xunit`, `NSubstitute`, `FluentAssertions`, `Microsoft.AspNetCore.SignalR.Client` (doar în `Kanabol.Api.Tests`, ca testele să se conecteze la hub ca un browser; decis 2026-11-07).

Pachete npm cheie: `@angular/*`, `@angular/material`, `@angular/cdk`, `@angular/localize`, `@microsoft/signalr`, `diff` (jsdiff, doar în chunk-ul task-urilor, pentru diferența descrierii din istoric, §4.4), `chart.js` 4.6.1 (fără wrapper Angular: un component `kb-chart` peste `<canvas>`, pentru rapoarte, cu doar piesele barelor înregistrate; grupul 18), `angular-eslint`, `typescript-eslint`, `prettier`, `vitest`, `husky`, `lint-staged`, `@commitlint/cli`, `@commitlint/config-conventional`. Fără pachet de markdown: descrierile și comentariile trec prin parserul propriu din `shared/ui/markdown-parser.ts` și `kb-markdown` (§16).

**Evităm**: MediatR și AutoMapper (ambele au trecut pe licență comercială în 2025), Moq (incidentul SponsorLink), Karma (abandonat de Angular), NgRx clasic (prea mult boilerplate pentru un singur dev).

---

## 3. Structura repo

```
kanabol/
├── .editorconfig                 # comun pentru C#, TS, JSON, YAML
├── .gitignore
├── .gitattributes                # eol=lf pentru scripturi
├── .husky/                       # hook-uri git (vezi §14)
├── .lintstagedrc.json
├── .prettierignore
├── commitlint.config.js
├── package.json                  # doar tooling de repo (husky, lint-staged, commitlint, prettier)
├── compose.yaml                  # servicii comune
├── compose.override.yaml         # dev (porturi expuse, volume locale) — încărcat automat
├── compose.prod.yaml             # prod (nginx, fără porturi inutile)
├── .env.example
├── scripts/
│   ├── setup.sh                  # instalare tooling + git config (rulat o dată după clone)
│   ├── verify.sh                 # ce rulează hook-ul: lint + teste
│   ├── test-all.sh               # suita completă back + front
│   ├── require-docker.sh         # oprește cu mesaj clar când Docker e oprit
│   ├── make-certs.sh             # CA proprie + certificatul serverului (§13.4)
│   ├── seq-retention.sh          # retenția de 30 de zile în Seq, o dată per volum
│   └── check-*.sh                # verificările de acceptanță ale grupurilor din Tasks.md (§14.7)
├── docs/
│   ├── SPEC.md                   # acest fișier
│   ├── UI_UX_SPEC.md             # spec-ul vizual, copie a notei din vault
│   ├── tls.md                    # cum se importă CA-ul pe stațiile colegilor
│   ├── images/                   # imaginile din UI_UX_SPEC.md
│   └── adr/                      # copii ale deciziilor din Obsidian, numerotate
├── CLAUDE.md                     # indică spre vault + SPEC; reguli de git
├── backend/
│   ├── CLAUDE.md                 # comenzi de build/run/test/migrare pentru backend
│   ├── Kanabol.sln
│   ├── Directory.Build.props     # setări comune tuturor proiectelor
│   ├── Directory.Packages.props  # versiuni de pachete centralizate
│   ├── Dockerfile
│   ├── src/
│   │   ├── Kanabol.Domain/
│   │   ├── Kanabol.Application/
│   │   ├── Kanabol.Infrastructure/
│   │   └── Kanabol.Api/
│   └── tests/
│       ├── Kanabol.Domain.Tests/
│       ├── Kanabol.Application.Tests/
│       └── Kanabol.Api.Tests/      # integrare: WebApplicationFactory + Testcontainers
└── frontend/
    ├── CLAUDE.md                 # comenzi serve/test/lint pentru frontend
    ├── angular.json
    ├── package.json
    ├── eslint.config.js
    ├── .prettierrc
    ├── .lintstagedrc.json        # lint-staged pentru frontend (§14.5)
    ├── proxy.conf.json           # ng serve → API local
    ├── Dockerfile
    ├── nginx-app.conf            # ce au comun dev și prod (§13.2)
    ├── nginx.conf                # dev: portul 80
    ├── nginx.prod.conf           # prod: 443, plus redirect de pe 80
    └── src/
```

Numele proiectului e **Kanabol**: namespace C# `Kanabol.*`, proiect Angular `kanabol` (prefix selector `kb`), imagini Docker `kanabol-api` / `kanabol-web`.

Fiecare nivel are un `CLAUDE.md` scurt, cu comenzi și reguli locale (deja existent). Cel din root trimite spre vault și spre `docs/SPEC.md`; `backend/CLAUDE.md` și `frontend/CLAUDE.md` se actualizează pe măsură ce apar comenzi noi (teste, migrări, lint) — ele sunt „cheat sheet”-ul, SPEC-ul e motivația.

### 3.1 Starea repo-ului la 2026-11-04 și diferențele față de spec

Repo-ul există la `E:\Workspace\kanabol` cu un schelet generat. Ce e deja aliniat cu spec-ul și ce trebuie ajustat:

| Ce există | Stare | Acțiune |
|---|---|---|
| Monorepo `backend/` + `frontend/`, `.gitignore` root (dotnet + node + `.env` + IDE) | ✔ | Adaugă `certs/` la `.gitignore` |
| `CLAUDE.md` root → vault, `backend/CLAUDE.md`, `frontend/CLAUDE.md` | ✔ | Adaugă referința la `docs/SPEC.md` și rezumatul convențiilor în cel din root |
| `backend/Kanabol.Api` (net10, minimal API „Hello World”), direct sub `backend/` | parțial | Mută în `backend/src/Kanabol.Api`, adaugă `Domain`/`Application`/`Infrastructure`, treci pe controllers (§4) |
| `Kanabol.sln` cu configurații x64/x86 | ✔ | Lasă doar `Any CPU` (curățenie, opțional) |
| Fără `Directory.Build.props` / `Directory.Packages.props` | lipsă | Creează conform §12.2 |
| API local pe `http://localhost:5200` (profil `http` în `launchSettings.json`) | ✔ | Spec-ul adoptă 5200 pentru dev local; 8080 rămâne doar în Docker (§13.3) |
| `appsettings.json` cu `Logging` default | de înlocuit | Serilog (§8.1) |
| Angular 22.2, standalone, **zoneless** (fără `zone.js`), Vitest 5 + jsdom | ✔ | — |
| Prefix `app`, selector `app-root` | de schimbat | `prefix: "kb"` în `angular.json`, `kb-root` |
| Stiluri CSS (`styles.css`, `app.css`) | de schimbat | SCSS: Angular Material 22 se tematizează prin Sass (`mat.theme()`); schimbă `schematics` + redenumește cele două fișiere |
| `.prettierrc` (printWidth 100, singleQuote, parser angular pe html) | ✔ | Adaugă `trailingComma: "all"` (e default în Prettier 3, dar explicit) |
| `.editorconfig` doar în `frontend/` | de mutat | Un singur `.editorconfig` în root, cu secțiunile C# din §12.1; cel din `frontend/` se șterge |
| `tsconfig.json` fără `strict` și `strictTemplates` explicite | de verificat | TypeScript 6 are `strict: true` implicit; adaugă oricum explicit `"strict": true` și `"strictTemplates": true` ca să nu depindă de default-uri |
| Fără ESLint | lipsă | §12.3 |
| `angular.json` → `schematics: {}` | de completat | `@schematics/angular:component`: `style: scss`, `changeDetection: OnPush`; (fără sufixe e deja default în v20+) |
| Fără `package.json` în root, fără `.husky/` | lipsă | §14 |

Regulă din `CLAUDE.md`-ul existent, preluată în spec: **commit-urile nu menționează Claude și nu au `Co-Authored-By` sau altă atribuire AI** (§7.5).

---

## 4. Backend — arhitectură

### 4.1 Clean Architecture, patru proiecte

```
Kanabol.Api ──────────► Kanabol.Application ◄────── Kanabol.Infrastructure
                              │
                              ▼
                        Kanabol.Domain
```

Regula dependențelor: săgețile merg doar spre interior. `Domain` nu referă nimic. `Application` referă doar `Domain`. `Infrastructure` implementează interfețele din `Application`. `Api` le leagă pe toate prin DI.

| Proiect | Conține | Nu conține |
|---|---|---|
| **Domain** | Entități, value objects, enum-uri, domain events, excepții de domeniu, reguli invariante (metode pe entități) | EF, HTTP, DI, orice pachet extern |
| **Application** | Commands/Queries + handlere, DTO-uri, validatoare FluentValidation, interfețe (`IAppDbContext`, `IFileStorage`, `IClock`, `ICurrentUser`, `IBoardNotifier`), `Result<T>`, autorizare la nivel de proiect | Implementări de infrastructură |
| **Infrastructure** | `AppDbContext`, configurări EF, migrări, `FileStorage` pe disc, JWT, hashing parole, `SystemClock`, background jobs (alerte) | Logică de business |
| **Api** | Controllers, filtre, middleware (correlation id, exception handler), `Program.cs`, autorizare pe permisiuni, SignalR hubs și notifier-ul lor (`HubBoardNotifier` peste `IHubContext`, §9; Infrastructure nu poate depinde de Api; decis 2026-11-07) | Logică de business, acces direct la DbContext |

### 4.2 CQRS light (fără MediatR)

Fiecare caz de utilizare e o clasă `Command` sau `Query` cu un handler dedicat. Controllers injectează handlerul direct — fără dispatcher, fără reflecție.

```csharp
// Application/Abstractions/Messaging/
public interface ICommand<TResult> { }
public interface IQuery<TResult> { }

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken ct);
}
```

```csharp
// Application/Features/Tasks/Commands/MoveTask/
public sealed record MoveTaskCommand(Guid TaskId, Guid TargetColumnId, int Position) : ICommand<Unit>;

public sealed class MoveTaskCommandValidator : AbstractValidator<MoveTaskCommand> { ... }

public sealed class MoveTaskCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    IClock clock) : ICommandHandler<MoveTaskCommand, Unit>
{
    public async Task<Result<Unit>> Handle(MoveTaskCommand cmd, CancellationToken ct)
    {
        var task = await db.TaskItems
            .Include(t => t.History)
            .FirstOrDefaultAsync(t => t.Id == cmd.TaskId, ct);
        if (task is null) return TaskErrors.NotFound(cmd.TaskId);

        var access = await projectAccess.EnsureRealMemberAsync(task.ProjectId, currentUser.Id, ct); // orice schimbare cere apartenența reală (§4.4)
        if (access.IsFailure) return access.Error;

        task.MoveTo(cmd.TargetColumnId, cmd.Position, currentUser.Id, clock.UtcNow); // ridică TaskMovedEvent
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

Reguli:
- Un handler = o tranzacție = un `SaveChangesAsync`. `DbContext` este Unit of Work; **nu** facem Repository generic peste EF (abstracție inutilă). `IAppDbContext` expune doar `DbSet`-urile și `SaveChangesAsync`. **Excepția creării unui task (decis 2026-11-07):** numărul vine din `ITaskNumbers.SaveNewTaskAsync` (`Application/Abstractions`, implementat în `Infrastructure/Persistence/TaskNumbers.cs`), care deschide tranzacția, face `UPDATE projects SET next_task_number = next_task_number + 1 WHERE id = … AND archived_at IS NULL RETURNING …`, construiește task-ul cu numărul primit, îl salvează și face commit; `IAppDbContext` rămâne cum e. `UPDATE`-ul ține rândul proiectului până la commit, deci creările din același proiect trec una după alta și nici două poziții nu se suprapun. Un proiect arhivat între citire și `UPDATE` dă 409 `Project.Archived`, o coloană ștearsă între timp 400 pe `columnId`, iar numărul se pierde odată cu tranzacția.
- Queries citesc cu `AsNoTracking()` și proiectează direct în DTO (`Select(t => new TaskItemDto(...))`). Fără a încărca entități pentru citire.
- Validarea de **formă** (câmpuri obligatorii, lungimi) stă în `AbstractValidator<T>` și rulează printr-un decorator de handler (Scrutor). Validarea de **business** (ex. „nu poți atribui unui non-membru”) stă în entitate sau handler și întoarce `Result.Failure`.
- Mapping manual (constructor de DTO sau metodă statică `FromEntity`). Fără AutoMapper.

### 4.3 Pattern-uri folosite în backend

| Pattern | Unde | De ce |
|---|---|---|
| **Result pattern** | `Application` returnează `Result<T>` cu `Error(Code, Message, ErrorType)` | Erorile așteptate (not found, forbidden, conflict) nu sunt excepții; API le mapează la ProblemDetails |
| **Domain events** | Entitățile acumulează `IDomainEvent`; un `SaveChangesInterceptor` le publică după commit | Decuplează istoricul, alertele, SignalR de handlere |
| **Decorator** (prin Scrutor) | `ValidationDecorator<TCommand>`, `LoggingDecorator` în jurul handlerelor | Cross-cutting fără MediatR pipeline |
| **Options pattern** | `JwtOptions`, `StorageOptions`, `AlertsOptions` cu `ValidateDataAnnotations().ValidateOnStart()` | Config tipată, eșuează la pornire dacă lipsește ceva |
| **Strategy** | `IPasswordHasher`, `IFileStorage` | Înlocuibile fără a atinge Application |
| **Aggregate** (ușor) | `TaskItem` deține `Comments`, `Attachments`, `History`; `Project` deține `Members`, `Columns` | Invariantele se aplică într-un singur loc |
| **Specification** | **nu** în v1 | Filtrele de query stau în handler; dacă apar duplicate, extragem metode de extensie `IQueryable` |
| **Background service** | `OverdueTasksWorker : BackgroundService` cu `PeriodicTimer`, în `Infrastructure`, pornit după migrații: la pornire și apoi la fiecare `Alerts:OverdueCheckInterval` (`AlertsOptions`; implicit 15 min, cel puțin 1 min, validat la pornire; `Alerts:OverdueCheckEnabled` implicit `true`, `false` în testele API) rulează, într-un scope propriu și fără utilizator, `NotifyOverdueTasksCommand` din `Application`, pe care testele îl rulează direct: task-uri cu `due_date < now`, `completed_at` nul, nearhivate, din proiecte nearhivate; pentru fiecare, asignații și owner-ii proiectului care sunt membri activi (ca la `CommentAdded`), fiecare o singură dată, și doar cei fără o notificare `TaskOverdue` pe perechea task + persoană, deci un asignat adăugat mai târziu pe un task deja întârziat o primește și el, iar nimeni n-o primește a doua oară, nici când deadline-ul se mută în viitor și trece iar → `Notification(TaskOverdue)` cu mesajul „KAN-1: Login SSO is overdue”, toate într-o singură salvare → `NotificationCreatedEvent` → SignalR `user:{id}`. La prima rulare după deploy task-urile deja întârziate primesc alerta o dată. O rulare care pică e logată și nu oprește worker-ul (decis 2026-11-08). Cum s-a implementat (grupul 17, decis 2026-11-08): intervalul e și cel mult o zi, validat la pornire ca minimul (un interval mai lung ar întârzia alertele cu zile, iar `PeriodicTimer` nu trece de ~49 de zile); valorile implicite stau și în `appsettings.json` (`Alerts`), ca `Storage`; worker-ul e înregistrat după `DatabaseInitializer` (serviciile găzduite pornesc pe rând) și scrie la pornire „The overdue check runs now and every …” sau „The overdue check is turned off”; fiecare rulare scrie la `Information` câte notificări a făcut, pentru câte task-uri (și 0), iar una căzută e `Error` (excepția, sau codul unui `Result` eșuat). „Owner” e rândul `project_members` cu rolul `Owner`. Perechea task + persoană e păzită de un `NOT EXISTS` în query, fără index unic: cu o singură instanță, cum cere §5.3, nu se dublează | Alerte deadline in-app, fără scheduler extern |
| **Domain event handlers** pentru notificări | `TaskAssignedEvent` (cu `AddedIds` și `RemovedIds`, ridicat la orice schimbare a listei, și când doar pleacă cineva, ca SignalR-ul din §9 să afle și de acelea) → `Notification(TaskAssigned)` pentru fiecare id din `AddedIds`, nu și pentru cei care erau deja pe task, nici pentru cel care face schimbarea, toate notificările într-o singură salvare (decis 2026-11-07); `CommentAddedEvent` → `Notification(CommentAdded)` pentru asignații și creatorul task-ului care îl mai pot deschide, adică membrii activi ai proiectului (mesajul poartă titlul task-ului, deci cine a fost scos din proiect sau dezactivat nu o primește), fiecare o singură dată, fără autorul comentariului, cu mesajul „Ana commented on KAN-1: Login SSO”, ca la `TaskAssigned`; editarea unui comentariu nu notifică (decis 2026-11-07, membrii activi 2026-11-08); ambele → `NotificationCreatedEvent` → SignalR `user:{id}`. O creare cu asignați ridică și ea `TaskAssignedEvent` (decis 2026-11-07). `Message` e în engleză, ca API-ul („Administrator assigned you KAN-1: Login SSO”, tăiat la 500 de caractere); pagina notificărilor își compune textul din `Type` și task (UI §7.6; decis 2026-11-06) | Clopoțelul e util din prima zi, nu doar la întârzieri |

### 4.4 Autorizare

**Nivel aplicație — permisiuni.** Codul verifică permisiuni, niciodată nume de roluri. Lista inițială (constante în `Kanabol.Domain.Authorization.Permissions`):

```
users.manage        roles.manage        projects.create
projects.view_all   projects.manage     tasks.create
tasks.archive       reports.view
```

`tasks.create` se verifică pe lângă apartenența la proiect: creezi task-uri doar în proiectele în care ești membru și doar dacă rolul tău are permisiunea (UI §7.4). Apartenența e cea reală, un rând în `project_members`, nu cea derivată din `projects.view_all` sau `projects.manage`: cine administrează un proiect străin se adaugă întâi ca membru. Un rol poate deci vedea un proiect fără să poată adăuga task-uri în el.

**Schimbarea unui task.** Orice schimbare a unui task (titlu, descriere, asignați, prioritate, deadline, mutare, arhivare) cere aceeași apartenență reală ca la creare: rând în `project_members` și cont activ (`IProjectAccess.EnsureRealMemberAsync`). `projects.view_all` și `projects.manage` citesc, dar schimbă abia după ce se adaugă ca membri. Restaurarea cere `tasks.archive` (pe endpoint, din token) și să vadă proiectul, ca un admin să restaureze fără să intre în proiect. Într-un proiect arhivat orice schimbare primește 409 `Project.Archived`. Un task poate avea oricâți asignați, fără limită în afara numărului de membri (decis 2026-11-07): se asignează doar membri activi ai proiectului (400 pe `assigneeIds`, și pentru un membru dezactivat, un id repetat, o listă lipsă sau `null`; lista întreagă se refuză atunci și nimic nu se schimbă); un asignat dezactivat după asignare rămâne pe task până îl scoate cineva. Schimbarea asignaților trece prin `TaskChanges`, ca orice altă proprietate (409 `Task.ChangedMeanwhile` pe `xmin`, §5.3). Vechiul `PUT /api/tasks/{id}/assignee` a dispărut (404; decis 2026-11-07). `includeArchived=true` fără `tasks.archive` primește 403 `Auth.PermissionRequired`, la listă, la `GET` după id și după cheie (decis 2026-11-06).

**Endpoint-urile task-urilor** sunt în tabelul din `backend/CLAUDE.md`: `POST /api/projects/{id}/tasks` (fără coloană, în prima; la capătul coloanei), `GET /api/projects/{id}/tasks` (`Page`, implicit după cheie descrescător, `sort=` key / title / column / priority / dueDate / assignee / updated cu `:asc` sau `:desc`, filtrele `assigneeId`, `priority` repetat, `overdue`, `search`, `includeArchived`; `assigneeId` potrivește orice task care îl are printre asignați, iar sortarea după asignat ia primul nume în ordine alfabetică, `upper(display_name)` apoi cheia, cu task-urile fără asignat ultimele; la fel e ordinea asignaților din DTO-uri. Sortările de text compară octet cu octet în v1, ca la titlu, deci un nume sau un titlu care începe cu Ă, Â, Î, Ș sau Ț ajunge după Z; colația `ro-x-icu` pe toate sortările de text e un task la coada listei, decis 2026-11-07), `GET /api/tasks/{id}`, `GET /api/tasks/by-key/{key}` (în orice majuscule), `PUT /api/tasks/{id}/title`, `…/description`, `…/priority`, `…/due-date`, `…/assignees` (câte o proprietate, cum le schimbă panoul în loc; `assignees` primește `assigneeIds`, lista întreagă, care o înlocuiește pe cea veche, iar `POST` primește tot `assigneeIds`), `PATCH …/position`, `DELETE` (arhivează, 204) și `POST …/restore`; `GET /api/tasks/{id}/history` (grupul 16) dă, pentru fiecare intrare `Assignees`, și numele id-urilor ei, chiar scoși din proiect, ca timeline-ul să le arate (decis 2026-11-07; regulile lui în paragraful „Istoricul unui task” de mai jos). „Întârziat” = deschis, după deadline, în afara unei coloane `is_done`; căutarea găsește o parte din titlu, `%` și `_` fiind litere simple. Coduri: `Task.NotFound`, `Task.ChangedMeanwhile` (pe `xmin`-ul task-ului, și la două mutări prin aceeași coloană) (decis 2026-11-06).

**Coloanele și board-ul** (grupul 13, decis 2026-11-07). Coloanele le schimbă ownerul sau `projects.manage` (ca Setările, prin `ProjectChanges`) și le citește oricine vede proiectul: `GET`/`POST /api/projects/{id}/board-columns`, `GET`/`PUT`/`DELETE …/board-columns/{columnId}` și `PATCH …/board-columns/{columnId}/position` (răspunde toate coloanele în noua ordine); tabelul e în `backend/CLAUDE.md`. O coloană nouă vine la capătul din dreapta; numele are 1–100 de caractere, fără caractere ascunse. „Goală” înseamnă fără niciun task, nici arhivat, pentru că task-ul arhivat își păstrează coloana și revine în ea la restaurare (409 `Column.NotEmpty`); ultima coloană nu se șterge (409 `Column.LastColumn`), pentru că orice task pornește într-o coloană; coloana altui proiect e 404 `Column.NotFound`. O coloană care devine `is_done` completează task-urile deschise din ea, iar una care încetează să fie `is_done` le redeschide, fără istoric (doar mutările intră în istoric); cele arhivate rămân cum erau. O schimbare de coloane salvează și proiectul, ca la membri, deci a doua din două schimbări simultane primește 409 `Project.ChangedMeanwhile`; o coloană ștearsă în timp ce cineva mută sau creează un task în ea dă 400 pe `columnId` mutării sau creării, ori 409 `Column.NotEmpty` ștergerii, după cine ajunge primul. Rămâne o cursă de câteva milisecunde, acceptată ca cele de la login: un task mutat într-o coloană chiar în clipa în care ea devine `is_done` poate rămâne deschis acolo. Board-ul se citește din `GET /api/projects/{id}/board` (pentru cine vede proiectul): coloanele și toate task-urile deschise, fără pagini, fiecare cu locul, asignații și numărul de comentarii și atașamente, plus `canMove` (membru real al unui proiect deschis) și `canManage` (owner sau `projects.manage`, proiect deschis); filtrele board-ului lucrează în browser (UI §7.4).

**Comentariile și atașamentele** (grupul 15, decis 2026-11-07, completat 2026-11-08). `GET /api/tasks/{id}/comments` dă toate comentariile task-ului, de la cel mai vechi, fără pagini, cui vede proiectul, fiecare cu `canEdit`; `POST /api/tasks/{id}/comments` adaugă unul (201, cu `Location` spre `GET /api/comments/{id}`, care dă un comentariu cu regulile listei; panoul nu îl folosește) și `PUT /api/comments/{id}` îl schimbă doar autorului lui (403 `Comment.NotYours`, și pentru owner), amândouă pentru un membru real al unui proiect deschis (409 `Project.Archived`), pe un task deschis (409 `Task.Archived`); corpul e markdown de bază, ca descrierea, de cel mult 5.000 de caractere; fără ștergere în v1. `POST /api/tasks/{id}/attachments` urcă un fișier pe cerere (multipart, câmpul `file`), cu aceleași condiții ca un comentariu, și 400 pe `file` peste 25 MB sau cu o extensie din afara listei (regulile în §10); lista atașamentelor (nume, mărime, cine, când, `canDelete`) vine în DTO-ul task-ului; `GET /api/attachments/{id}` îl descarcă oricui vede proiectul; `DELETE /api/attachments/{id}` (204) îl șterge de tot, și de pe disc, pentru cine l-a urcat, cât mai e membru real (altfel 403 `Project.MemberRequired`), sau pentru `tasks.archive`, numai pe un task deschis dintr-un proiect deschis (409 `Task.Archived` sau `Project.Archived`, și pentru `tasks.archive`). Pe un task arhivat comentariile și fișierele sunt vizibile doar cui are `tasks.archive`, ca task-ul însuși: oricine altcineva primește 404 `Task.NotFound` la comentarii și `Attachment.NotFound` la fișier. Un comentariu sau un atașament nu e o schimbare a task-ului: nu scrie istoric, nu mută `updated_at` („Actualizat la” rămâne) și nu trece prin `xmin`. Tabelele sunt în `backend/CLAUDE.md`. Coduri: `Comment.NotFound`, `Comment.NotYours`, `Attachment.NotFound`, `Attachment.NotYours`.

**Istoricul unui task** (grupul 16, decis 2026-11-08). `GET /api/tasks/{id}/history` dă intrările `task_history_entries` ale task-ului ca `Page`, de la cea mai nouă, cu `pageSize` implicit 50, cui vede proiectul, iar pe un task arhivat doar cui are `tasks.archive` (ceilalți 404 `Task.NotFound`), ca la comentarii. Fiecare intrare vine cu `field`, `oldValue`, `newValue`, `changedAt` și `changedBy` (id și numele afișat, și pentru un cont dezactivat), iar id-urile din valori vin și cu numele lor: pentru `Assignees` numele utilizatorilor, și pentru cei scoși din proiect; pentru `ColumnId` numele coloanelor, fără nume pentru o coloană ștearsă între timp, pe care timeline-ul o arată „coloană ștearsă”. Istoricul nu se trimite pe hub: panoul îl citește când se deschide tab-ul și îl recitește după fiecare schimbare salvată din panou (UI §7.5). Forma intrării (decis 2026-11-08): `id`, `field`, `oldValue`, `newValue`, `changedAt`, `changedBy` (`userId`, `displayName`) și `names`, un obiect id → nume pentru fiecare id din valorile `Assignees` și `ColumnId`, cu `null` pentru o coloană ștearsă, gol la celelalte câmpuri; un text care nu e id rămâne fără nume. Intrările din aceeași clipă vin după id, descrescător, ca nicio intrare să nu apară pe două pagini; `page` și `pageSize` în afara lui 1–100 dau 400 pe ele, ca la celelalte liste. Textele descrierii (grupul 16b, decis 2026-11-08): în listă o intrare `Description` vine cu `oldValue` și `newValue` nule, ca o pagină să nu care până la 50 × 40 kB; `GET /api/tasks/{id}/history/{entryId}` dă o intrare întreagă, cu textele, cu regulile listei (404 `History.NotFound` pentru una care nu e a task-ului), și e cerută doar când cineva deschide diferența (UI §7.5). Diferența se calculează în browser, pe sursa markdown, la nivel de cuvânt, cu pachetul `diff` (§2); API-ul nu calculează nimic. Cum s-a implementat (grupul 16b, decis 2026-11-08): lista nici nu citește textele descrierii din baza de date (un `CASE` în query), deci pagina nu le cară nici între PostgreSQL și API; intrarea are aceeași formă ca în listă, cu `names` pentru `Assignees` și `ColumnId`, și merge pentru orice câmp, nu doar `Description`; întâi se verifică task-ul, ca la listă (404 `Task.NotFound`, 403 `Project.MemberRequired`), apoi intrarea, deci `History.NotFound` e și pentru un id care nu există deloc; un `entryId` care nu e UUID e 404 de la rută. Pachetul din §2 e `diff` 9 (jsdiff).

**Notificările** (grupul 17, decis 2026-11-08). Oricine e autentificat își vede doar notificările lui, fără vreo permisiune. `GET /api/notifications` le dă ca `Page`, de la cea mai nouă (la aceeași clipă după id, descrescător), cu `pageSize` implicit 20 și cel mult 100 (`page` și `pageSize` în afara limitelor dau 400, ca la celelalte liste), cu filtrele `unread=true` și `type` (repetat: `TaskOverdue`, `TaskAssigned`, `CommentAdded`; o valoare necunoscută dă 400 pe `type`); fiecare vine cu `id`, `type`, `message`, `createdAt`, `readAt` și `task` (`id`, `key`, `title`, titlul de acum; `null` pentru o notificare fără task), și rămâne în listă și când omul nu mai vede task-ul (deschiderea lui dă 403 sau 404 ca azi). `GET /api/notifications/unread-count` dă `{ count }`. `POST /api/notifications/{id}/read` dă 200 cu notificarea, idempotent (al doilea apel păstrează primul `readAt`), și 404 `Notification.NotFound` pentru una inexistentă sau a altcuiva; `POST /api/notifications/read-all` dă 200 cu `{ count }`, câte a marcat acum. O notificare nu e o schimbare a task-ului: fără istoric, fără `updated_at`. Tabelul e în `backend/CLAUDE.md`. Cum s-a implementat (grupul 17, decis 2026-11-08): `unread` care nu e `true`/`false` dă 400 pe `unread`, ca `type` necunoscut pe `type`; un `id` care nu e UUID e 404 de la rută; `task` vine și pentru un task arhivat sau dintr-un proiect arhivat, `null` doar fără task; `read` recitește notificarea prin același query ca lista; `read-all` e un singur `UPDATE … WHERE read_at IS NULL`, fără `SaveChanges`, deci numără exact ce a marcat el; cât `MustChangePassword` e activ, 403 `Auth.PasswordChangeRequired`, ca peste tot.

**Panoul principal și task-urile mele** (grupul 17a, decis 2026-11-08). Oricine e autentificat, fără vreo permisiune; „ale mele” înseamnă task-urile pe care omul e asignat, nearhivate, din proiecte nearhivate, iar `projects.view_all` nu lărgește nimic aici. `GET /api/me/summary?weekStart=<instant ISO>` dă `openTasks` (ale mele cu `completed_at` nul), `overdueTasks` (dintre cele deschise, cu `due_date < now`), `completedThisWeek` (ale mele cu `completed_at` de la `weekStart` încoace; clientul trimite lunea 00:00 în ora lui) și `activeProjects` (proiecte nearhivate în care e membru); `weekStart` e obligatoriu și cel mult acum, altfel 400 pe el. `GET /api/me/tasks` dă ale mele ca `Page`, cu filtrele și sortările lui `GET /api/projects/{id}/tasks` în afară de `assigneeId` (`includeArchived` tot doar cu `tasks.archive`), plus `open=true` pentru cele cu `completed_at` nul; implicit după deadline crescător, cele fără deadline la coadă, apoi cheia descrescător; fiecare rând are și `projectId`, `projectKey` și `projectName`. `GET /api/projects` primește `mine=true` (doar proiectele în care omul e membru, nu și cele văzute prin `projects.view_all` sau `projects.manage`), iar fiecare proiect din listă are și `completedTaskCount` (task-uri nearhivate cu `completed_at`), ca bara de progres să fie finalizate / (deschise + finalizate). Panoul principal citește `me/summary`, `me/tasks?open=true&pageSize=8`, `notifications?pageSize=5` și `projects?mine=true`, fără endpoint comun și fără hub (UI §7.2). Cum s-a implementat (grupul 17a, decis 2026-11-09): „ale mele” sunt, în plus, doar task-urile din proiecte în care omul e încă membru, cu cont activ: cine e scos dintr-un proiect rămâne asignat pe task-urile lui, dar ele nu mai intră în numere și în listă, pentru că panoul ar duce la un proiect pe care nu-l mai poate deschide (ca notificările, doar pentru membri activi); un cont dezactivat primește zero și o pagină goală. `weekStart` se primește cu orice offset și se trece în UTC (Npgsql scrie doar UTC); lipsă, greșit sau strict după ceasul serverului dă 400 pe el, fără toleranță, pentru că clientul trimite lunea trecută. `open` sau `mine` care nu sunt boolean dau 400; `assigneeId` trimis la `me/tasks` e ignorat; în `me/tasks` cheia înseamnă cheia proiectului, apoi numărul, iar egalitățile oricărei sortări merg după cheie descrescător, în orice direcție (în lista proiectului rămân în direcția sortării). `overdue=true` din listă rămâne ca la lista proiectului (deschis, după deadline, în afara unei coloane `is_done`), iar `overdueTasks` din sumar numără `completed_at` nul și `due_date < now`, adică la fel în afara cursei de milisecunde. `mine=true&includeArchived=true` dă și proiectele arhivate în care e membru; `completedTaskCount` numără task-urile nearhivate cu `completed_at`, și cu `includeArchived`. Lista proiectului și „Task-urile mele” împart filtrele, rândurile și sortările (`TaskListRows`, `TaskQueries.MyTasks`); în Application stau `Features/Dashboard/` (`GetMySummaryQuery`, `MySummaryDto`) și `Features/Tasks/Queries/GetMyTasks/`, iar `MeController` servește `api/me`.

**Un utilizator are exact un rol** (`users.role_id`, NOT NULL). Rolul grupează permisiuni (`role_permissions`). Dacă cineva are nevoie de drepturi combinate, adminul creează un rol nou — nu atribuim mai multe roluri aceleiași persoane. Schimbarea rolului invalidează refresh token-urile utilizatorului (permisiunile din JWT se reîmprospătează la următorul login/refresh).

Permisiunile ajung în JWT ca claims `permission`. Pe API: `[HasPermission(Permissions.ProjectsViewAll)]` — un `IAuthorizationPolicyProvider` custom construiește politica `Permission:<name>` la cerere, fără să le înregistrăm pe fiecare în `Program.cs`.

**Cine poate da ce.** Nimeni nu dă mai mult decât are. Un rol se creează, se editează sau se șterge doar de cineva care are toate permisiunile lui, cele vechi și cele noi (403 `Role.PermissionsBeyondYours`). Un utilizator primește, la creare sau la schimbarea rolului, doar un rol ale cărui permisiuni le are apelantul (403 `User.RoleBeyondYours`), iar nimeni nu editează, nu mută în alt rol și nu dezactivează pe cineva care are permisiuni în plus față de el (403 `User.PermissionsBeyondYours`). Altfel `users.manage` singur ar duce la `Admin`, iar `roles.manage` ar putea goli rolul cuiva aflat mai sus, ca apoi să-l dezactiveze. `GET /api/users/assignable-roles` întoarce rolurile pe care apelantul le poate da. Adminul are toate permisiunile, deci limita nu-l atinge.

**Cont dezactivat sau retrogradat.** Administrarea utilizatorilor și a rolurilor nu se încrede în token: `CurrentPermissions` recitește din baza de date utilizatorul și permisiunile lui la fiecare cerere (401 `Auth.SessionEnded` dacă e dezactivat, 403 `Auth.PermissionRequired` dacă permisiunea a dispărut, 403 `Auth.PasswordChangeRequired` dacă `MustChangePassword` e activ, de pildă după o resetare), iar dezactivarea și resetarea parolei închid sesiunile (refresh token-urile sunt revocate). Celelalte endpoint-uri se bazează pe claims din access token, deci un cont dezactivat, retrogradat sau cu parola resetată le mai poate apela până expiră token-ul, cel mult 15 minute (§4.5). Acceptat așa în v1, pe 2026-11-05.

**Reactivare și resetare de parolă.** Amândouă cer `users.manage` și respectă limita de mai sus (403 `User.PermissionsBeyondYours`). `POST /api/users/{id}/activate` readuce utilizatorul cu rolul și parola de dinainte, fără sesiune nouă. `POST /api/users/{id}/reset-password` primește o parolă inițială (politica din §4.5), pune `MustChangePassword = true`, golește `failed_login_attempts` și `locked_until` și revocă refresh token-urile, într-o singură salvare; pe un cont dezactivat e permisă și nu-l reactivează. Nimeni nu-și resetează propria parolă pe calea asta (403 `User.OwnPasswordReset`): `change-password` cere parola curentă tocmai ca un access token furat să nu poată prelua contul.

**Nivel proiect — rol pe proiect.** `ProjectMember(ProjectId, UserId, ProjectRole)` cu `ProjectRole = Owner | Member`. Verificat în handler prin `IProjectAccess`:
- `EnsureMemberAsync(projectId, userId)` — trece dacă e membru **sau** are `projects.view_all` **sau** `projects.manage` (cine poate administra orice proiect îl poate și vedea).
- `EnsureOwnerAsync(projectId, userId)` — trece dacă e owner **sau** are `projects.manage`.

Refuzul e un 403 ProblemDetails: `code: Auth.PermissionRequired` pentru o permisiune lipsă (numele permisiunii apare doar în log, la `Warning`, nu în răspuns), `Project.MemberRequired` / `Project.OwnerRequired` pentru rolul pe proiect. `IProjectAccess` răspunde din baza de date, despre `userId`-ul primit, nu din claims: refuză utilizatorii dezactivați, nu verifică dacă proiectul există (handlerul răspunde 404 înainte) și păstrează owner-ii unui proiect arhivat.

**Proiecte și membri.** Endpoint-urile sunt în tabelul din `backend/CLAUDE.md`. `GET /api/projects` dă proiectele în care ești membru, sau toate cu `projects.view_all` ori `projects.manage`, citite din baza de date ca `EnsureMemberAsync`; nimic pentru un cont dezactivat; arhivatele doar cu `includeArchived=true`. `POST /api/projects` cere `projects.create`; creatorul e owner și proiectul pornește cu cele trei coloane (§5.1). `DELETE` arhivează (204) și `POST …/restore` readuce; nimic nu șterge un proiect. Un proiect arhivat se deschide și se citește, dar orice altă schimbare primește 409 `Project.Archived`. Membrii se adaugă dintre conturile active (400 pe `userId`), rolul circulă ca text, `Owner` / `Member`; `GET …/member-candidates?search=` dă până la 20 de conturi active din afara proiectului, ca un owner fără `users.manage` să găsească pe cine adaugă. Coduri: `Project.NotFound`, `Project.KeyTaken`, `Project.Archived`, `Project.AlreadyAMember`, `Project.MemberNotFound`, `Project.LastOwner`, `Project.ChangedMeanwhile` (decis 2026-11-06).

**Owner dezactivat.** Dezactivarea unui cont nu se refuză pentru proiectele lui: un admin poate dezactiva pe cineva care pleacă fără să-i predea întâi fiecare proiect. Token-ul încă valid al unui owner dezactivat nu mai deschide proiectul (`IProjectAccess` refuză conturile dezactivate, 403), iar el rămâne în lista de membri cu chip-ul „Dezactivat”. „Mereu un owner” înseamnă un owner **activ**: `RemoveMember` și `ChangeMemberRole` numără doar ownerii activi care rămân, deci un owner nu poate ieși sau coborî la membru când singurul alt owner e dezactivat (409 `Project.LastOwner`); handlerul dă entității id-urile membrilor activi. Un proiect rămas fără owner activ prin dezactivare se administrează prin `projects.manage`, care adaugă întâi un owner nou; un semn „Fără owner activ” în lista de proiecte e amânat (decis 2026-11-06; numărarea ownerilor activi se aplică în grupul 12).

Invariante:
- `Project` are mereu cel puțin un owner activ (verificat în `Project.RemoveMember` / `ChangeMemberRole`, cu id-urile membrilor activi de la handler; §4.4 „Owner dezactivat”). La cereri simultane ține `xmin`-ul proiectului: o schimbare a membrilor salvează și proiectul (`AuditingInterceptor`), deci din doi owneri care se scot unul pe altul în același timp al doilea primește 409 `Project.ChangedMeanwhile` și nu schimbă nimic (§5.3).
- Rolul `Admin` (`is_system = true`) nu poate fi șters, redenumit sau lipsit de permisiuni.
- Ultimul utilizator cu rolul `Admin` nu poate primi alt rol și nu poate fi dezactivat (verificat în handlerele `ChangeUserRole` și `DeactivateUser`, prin `LastAdminGuard`). O numărare nu ajunge când cererile se suprapun: `Role.HoldersVersion` crește de câte ori cineva pleacă din rol și e concurrency token, la fel `users.role_id` și `users.is_active`, deci a doua din două salvări simultane, sau una făcută pe o citire veche, primește 409 `User.ChangedMeanwhile`. Fără `is_active`, o cerere care citise un admin dezactivat îl putea scoate din `Admin` după ce fusese reactivat, iar ultimul alt admin plecase.
- Utilizatorii nu se șterg, se dezactivează (`is_active = false`): rămân referențiați din istoric, comentarii și rapoarte.

### 4.5 Autentificare

**Token-uri**

- `POST /api/auth/login` → access token JWT (15 min, în body) + refresh token (7 zile) în cookie `refresh_token`: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
- Access token-ul are claims `sub`, `permission` (câte unul pe permisiune) și `must_change_password`, doar când e cazul. E semnat HS256 și se validează fără toleranță de ceas.
- `POST /api/auth/refresh` rotește refresh token-ul (cel vechi devine invalid; refolosirea unuia invalidat revocă toată familia — detecție de furt). Două cereri simultane cu același token contează tot ca refolosire: `revoked_at` e concurrency token, deci a doua salvare nu mai găsește rândul.
- `POST /api/auth/logout` revocă familia sesiunii curente și șterge cookie-ul. Răspunde 204 și fără cookie.
- Login, refresh și logout sunt anonime, pentru că sunt tot ce-i rămâne unui client cu access token-ul expirat. Cookie-ul dovedește cine cheamă, iar `SameSite=Strict` îl ține departe de alte site-uri.
- Refresh token-urile se stochează hash-uite (SHA-256) în `refresh_tokens`. Curățarea (grupul 19, decis 2026-11-09): `RefreshTokensCleanupWorker` în Infrastructure, ca `OverdueTasksWorker` (după `DatabaseInitializer`, la pornire și apoi la fiecare `Auth:RefreshTokenCleanupInterval`, implicit o zi, cel puțin o oră; `Auth:RefreshTokenCleanupEnabled` false în testele API-ului), un singur `DELETE` pe rândurile expirate sau revocate de peste 30 de zile, cu numărul șters în log. Cum s-a implementat (grupul 19): worker-ul rulează `DeleteOldRefreshTokensCommand` din Application, ca `OverdueTasksWorker`; regula e `expires_at < now − 30 de zile` sau `revoked_at < now − 30 de zile`, cele de exact 30 de zile rămân; `Auth:RefreshTokenCleanupInterval` are și un maxim de 30 de zile (`PeriodicTimer` se oprește pe la 49, iar rândurile se țin 30), altfel API-ul nu pornește; secțiunea `Auth` e în `appsettings.json` (`AuthOptions`).
- Frontend ține access token-ul **doar în memorie** (semnal în `AuthStore`), niciodată în `localStorage`.
- SignalR primește token-ul prin query string `access_token` la handshake (standard), citit doar sub `/hubs` și doar când numele lui e în adresă exact cum a fost trimis (ASP.NET decodează `access%5Ftoken` în același nume, pe care redactarea din §8.1 nu l-ar vedea); sub `/api` nu deschide nimic. O conexiune se închide când îi expiră token-ul (`CloseOnAuthenticationExpiration`, `Close` cu `allowReconnect`), iar clientul se reconectează cu unul proaspăt, reîmprospătat când mai are cel mult 30 s, socotite din durata lui (`exp` minus `iat`) de la primire, nu din ceasul calculatorului, pe care API-ul, fără toleranță, l-ar prinde nesincronizat (decis 2026-11-07).
- Cookie-ul de refresh e al browserului, comun tab-urilor: când `sub`-ul token-ului primit la refresh nu e utilizatorul autentificat în tab (altcineva a intrat în cont în alt tab), tab-ul își încheie sesiunea. Limită cunoscută în v1: un tab cu board-ul deschis se reconectează la fiecare expirare, deci face refresh la 15 minute fără nicio acțiune și ține sesiunea în viață cât stă deschis (decis 2026-11-07).

**Parole**

- `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2, fără pachetul Identity complet).
- Politică: minim 10 caractere, maxim 256 (ca nimeni să nu pună serverul să hash-uiască un body întreg) și în afara listei `Features/Auth/common-passwords.txt`.
- `POST /api/auth/change-password` cere parola curentă, iar cea nouă trebuie să difere. Revocă toate refresh token-urile și pornește o sesiune nouă pentru dispozitivul curent, într-o singură salvare, apoi răspunde ca login-ul.
- O parolă curentă greșită e 400 pe câmpul `currentPassword`, nu 401, la care frontend-ul ar încerca un refresh, și se numără la lockout.
- Cât timp `MustChangePassword` e activ, token-ul deschide doar endpoint-urile publice și pe cele marcate `[AllowedBeforePasswordChange]` (`change-password` și `me`). Restul răspund 403 `Auth.PasswordChangeRequired`.

**Login refuzat**

- Un singur răspuns, 401 `Auth.InvalidCredentials`, pentru parolă greșită și pentru cont inexistent, dezactivat sau blocat, cu același timp de răspuns. Motivul real apare doar în loguri.
- Lockout: 15 minute după 10 eșecuri (`failed_login_attempts`, `locked_until`). Contorul crește într-un singur `UPDATE` pe rândul curent, ca încercările trimise simultan să nu se suprascrie.
- Rate limiting pe `/api/auth/login`: 5 încercări / minut / IP (`AddRateLimiter`, fixed window), cu răspuns 429 ProblemDetails `Auth.TooManyLoginAttempts` și `Retry-After`.
- Adresa clientului vine din `X-Forwarded-For`, doar pentru conexiuni din rețele private, unde stă nginx. nginx suprascrie header-ul pe `/api/` și pe `/hubs/`, deci clientul nu-și poate alege adresa (§13.2).

**Restul**

- `GET /api/auth/me` întoarce utilizatorul și permisiunile, citite din DB, nu din token.
- Orice controller cere un utilizator autentificat, dacă nu are `[AllowAnonymous]` (`MapControllers().RequireAuthorization()`), deci un atribut uitat închide endpoint-ul, nu-l deschide.

### 4.6 Erori

- Handlere: `Result.Failure(Error)` cu `ErrorType ∈ { Validation, NotFound, Conflict, Forbidden, Unauthorized, Failure }`.
- Controllers: extensie `result.ToActionResult()` → 200/201/204 sau `ProblemDetails` cu 400/404/409/403/401.
- Excepții neașteptate: `IExceptionHandler` global → 500 ProblemDetails cu `traceId` și `correlationId`, logate la `Error`. Detaliile excepției nu ies din API în prod.
- Validation → 400 cu `ValidationProblemDetails` (`errors: { field: [messages] }`).

---

## 5. Model de domeniu și EF Core (code first)

### 5.1 Entități principale

```
Role ──< RolePermission >── Permission
Role ──< User ──< RefreshToken
User ──< ProjectMember >── Project ──< BoardColumn
Project ──< TaskItem ──< Comment
                     ├──< Attachment
                     ├──< TaskHistoryEntry
                     └──< Notification (overdue etc.)
```

- `User`: `Email` (unic, păstrat cu litere mici), `DisplayName`, `PasswordHash`, `RoleId` (exact un rol; concurrency token, §4.4), `IsActive` (și el concurrency token, §4.4), `FailedLoginAttempts`, `LockedUntil?`, `MustChangePassword`.
- `Role`: `Name` (unic indiferent de majuscule, `ux_roles_lower_name`), `IsSystem` (true doar pentru `Admin`), `HoldersVersion` (crește când cineva pleacă din rol; concurrency token care ține invarianta ultimului `Admin`, §4.4), permisiuni prin `role_permissions`.
- `Project`: `Name`, `Key` (prefixul cheilor de task: 2–5 litere A–Z, scrise cu majuscule, unic și printre proiectele arhivate, `ux_projects_key`, ex. `KAN`; nu se schimbă după creare; 409 `Project.KeyTaken`), `Description?` (cel mult 2.000 de caractere, cu rânduri noi și tab-uri), `NextTaskNumber` (contorul din care se alocă numerele de task), `ArchivedAt?`, `ArchivedById?`; deține `Members` și `Columns` (la creare: De făcut / În lucru / Gata cu `is_done`, UI §11).
- `TaskItem` (nu `Task`, ca să nu se bată cu `System.Threading.Tasks.Task`): `Number` (unic în proiect, alocat crescător la creare din `Project.NextTaskNumber`, în aceeași tranzacție; numărul vine dintr-un singur `UPDATE projects SET next_task_number = next_task_number + 1 … RETURNING`, în tranzacția creării, fără `xmin` și fără reîncercări, deci oricâte creări simultane primesc numere diferite și niciun 409; decis 2026-11-06, de aplicat în grupul 13 în locul celor 3 reîncercări), `Title` (cel mult 300 de caractere), `Description` (markdown de bază, stocat ca text, cel mult 20.000 de caractere, cu rânduri noi și tab-uri), `Priority` (enum `Low|Medium|High|Critical`), `DueDate?`, `Assignees` (oricâți membri activi ai proiectului, în tabela `task_assignees`: `task_id`, `user_id`, `assigned_at`, `assigned_by_id?`, cheie primară pe `task_id, user_id`; coloana `assignee_id` dispare, migrarea `AddTaskAssignees` mută valorile existente, cu `assigned_at` = `coalesce(updated_at, created_at)` al task-ului și fără `assigned_by_id`, și face din intrările de istoric `AssigneeId` intrări `Assignees` (un id devine lista de unul, nimeni lista goală), ca timeline-ul să citească un singur format; `Down` lasă fiecărui task primul asignat și întoarce la `AssigneeId` intrările cu cel mult un id; `assigned_by_id` are cheie străină spre `users`, ca `archived_by_id`, iar `ix_task_assignees_user_id` ține filtrul; decis 2026-11-07), `ColumnId`, `Position`, `CompletedAt?`, `ArchivedAt?`, `ArchivedById?`. Cheia afișată e `{Project.Key}-{Number}`, ex. `KAN-123`; `GET /api/tasks/by-key/{key}` o rezolvă (UI §7.5). Numărul rămâne și după arhivare, nu se refolosește.
- `BoardColumn`: `Name`, `Position`, `IsDone` (marchează coloana „terminat”; intrarea unui task într-o astfel de coloană setează `CompletedAt`, ieșirea îl anulează). Fără WIP limit în v1.
- `TaskHistoryEntry`: `Field`, `OldValue`, `NewValue`, `ChangedById`, `ChangedAt`. Entitatea `TaskItem` o adaugă singură în metodele de modificare (`Rename`, `ChangeDescription`, `MoveTo`, `Assign`, `SetPriority`, `SetDueDate`, `Archive`, `Restore`) — istoricul e garantat, nu depinde de handler. `Field` e numele proprietății (`Title`, `Description`, `Assignees`, `Priority`, `DueDate`, `ColumnId`, `ArchivedAt`), valorile ca text, datele în ISO UTC la microsecundă, cât păstrează PostgreSQL, ca restaurarea să scrie exact clipa arhivării (decis 2026-11-08); o valoare care nu se schimbă nu scrie nimic. Asignații intră ca o singură intrare per schimbare, lista veche și lista nouă de id-uri, ordonate și separate prin virgulă, goală când nu e nimeni; timeline-ul arată numele (UI §7.5; decis 2026-11-07). La mutare intră doar schimbarea coloanei, pe care o citesc rapoartele: o reordonare în aceeași coloană ridică `TaskMovedEvent` fără istoric, iar task-urile din jur își schimbă locul fără istoric și fără eveniment. Crearea nu scrie istoric (decis 2026-11-06).
- `Notification`: `UserId`, `Type` (enum `TaskOverdue|TaskAssigned|CommentAdded`), `TaskId?`, `Message`, `CreatedAt`, `ReadAt?`. Cine le produce e în §4.3: worker-ul pentru `TaskOverdue`, handlerele de domain events pentru celelalte două. Livrate exclusiv in-app (listă + clopoțel + push prin SignalR). **Nu** definim `IEmailSender` sau alt canal în v1; dacă apare nevoia, se adaugă atunci ca un consumer suplimentar al acelorași domain events.

**Arhivare, nu ștergere.** `DELETE /api/tasks/{id}` arhivează (`ArchivedAt = now`), nu șterge rânduri. Global query filter `HasQueryFilter(t => t.ArchivedAt == null)` pe `TaskItem`; comentariile, atașamentele și istoricul rămân intacte și intră în rapoarte. Admin (permisiunea `tasks.archive`) vede task-urile arhivate cu `?includeArchived=true` (handler cu `IgnoreQueryFilters()`) și le poate restaura (`POST /api/tasks/{id}/restore`). Nu există hard delete în API; curățarea fizică (dacă va fi vreodată nevoie) e o operațiune de DBA, nu o funcție de produs. Proiectele urmează aceeași regulă (`archived_at` pe `projects`).

### 5.1.1 Productivitate (feature `Reports`)

Nu e entitate; sunt trei queries read-only, filtrabile pe interval (`from`, `to`), pe proiect și pe utilizator. Pagina globală de rapoarte cere `reports.view`. Owner-ul unui proiect le vede fără permisiune, dar doar pentru proiectul lui, din tab-ul „Rapoarte” al paginii de proiect (UI §7.3): query-urile acceptă deci fie `reports.view`, fie owner pe `projectId`-ul din filtru, care devine obligatoriu în al doilea caz.

| Metrică | Definiție | Sursă |
|---|---|---|
| **Task-uri finalizate** | Număr de task-uri cu `completed_at` în interval, grupate pe asignat și pe proiect; serie pe săptămână/lună. Un task cu mai mulți asignați se numără la fiecare dintre ei, deci totalul pe oameni poate depăși totalul pe proiect, ceea ce pagina spune (decis 2026-11-07) | `task_items.completed_at`, `task_assignees` |
| **Lead time / cycle time** | Lead: `completed_at − created_at`. Cycle: de la prima ieșire a task-ului din prima coloană a board-ului (poziția 0, de obicei „De făcut”) până la `completed_at`; un task creat direct în altă coloană pornește de la `created_at`. Nu există marcaj de coloană „în lucru”, regula e poziția. Plus timp mediu per coloană. Raportate ca medie și mediană | `task_history_entries` cu `field = 'ColumnId'` (reconstruim intervalele de ședere per coloană) |
| **Deadline** | Task-uri întârziate (`due_date < now` și neterminate), task-uri finalizate după deadline, rata de respectare = finalizate la timp / finalizate cu deadline | `due_date`, `completed_at` |

Toate exclud task-urile arhivate înainte de finalizare. Calculul se face în EF cu proiecții și `GroupBy`; dacă query-ul pe istoric devine lent, se materializează într-o tabelă `task_column_stays` (`task_id, column_id, entered_at, left_at`) întreținută din evenimentul `TaskMovedEvent` — decizie amânată până apar date reale.

**Endpoint-urile rapoartelor** (grupul 18, decis 2026-11-09). Trei endpoint-uri sub `/api/reports`, fiecare cu un DTO propriu (nu `Page`) și cu aceiași parametri: `from` și `to` (instante ISO, obligatorii, `from < to`, cel mult 366 de zile, altfel 400 pe ele), `projectId` și `userId` opționale; `completed` și `deadlines` primesc și `granularity=week|month` (implicit `week`). Seriile au bucket-uri care pornesc de la `from` și sar cu 7 zile sau cu o lună calendaristică, deci se aliniază la ce trimite clientul (lunea 00:00 sau ziua 1, în ora lui); fiecare bucket are `periodStart`. Autorizarea e în handler, nu pe controller: cine are `reports.view` vede orice (toate proiectele când nu e `projectId`, orice `projectId`, și arhivat); altfel `projectId` e obligatoriu (400 fără) și omul trebuie să poată administra proiectul (`EnsureOwnerAsync`: owner sau `projects.manage`, ca tab-ul din UI §7.3), altfel 403; un membru simplu primește 403. `userId` poate fi oricine, rezultatul e oricum pe proiect.

- `GET /api/reports/completed`: task-urile cu `completed_at` în `[from, to)`, arhivate sau nu (arhivarea după finalizare nu șterge munca), din proiecte arhivate sau nu când nu e `projectId`; cu `userId`, doar cele pe care omul e asignat. Răspunsul: `total` (task-uri), `byUser` (`userId`, `name`, `count`; un task cu mai mulți asignați la fiecare, iar unul fără asignat intră doar în `total` și `byProject`), `byProject` (`projectId`, `key`, `name`, `count`) și `series` (`periodStart`, `count`). Pagina spune că suma pe oameni poate depăși totalul.
- `GET /api/reports/cycle-time`: pe aceleași task-uri finalizate; `count`, `leadTime` și `cycleTime`, fiecare cu `averageSeconds` și `medianSeconds` (întregi), plus `perColumn` (`columnId`, `name`, `averageSeconds`, `medianSeconds`) doar când e `projectId`, din intrările `ColumnId` ale istoricului (șederea într-o coloană e de la intrare până la ieșire sau la `completed_at`; coloanele șterse nu apar). Cycle pornește la prima ieșire din coloana de pe poziția 0, altfel de la `created_at`. Mediana nu iese din EF: handler-ul proiectează rândurile (`created_at`, `completed_at`, intrările `ColumnId`) și calculează media și mediana în memorie, intervalul fiind limitat.
- `GET /api/reports/deadlines`: `overdue` (task-uri deschise, nearhivate, cu `due_date < now`, pe proiectul și omul din filtru, indiferent de interval: e starea de azi), `completedLate` (finalizate în interval cu `completed_at > due_date`), `completedOnTime` (finalizate în interval cu `due_date` și la timp), `onTimeRate` (`onTime / (onTime + late)`, null când niciun finalizat n-are deadline) și `series` (`periodStart`, `onTime`, `late`).

Clientul le arată în `features/reports/`, cu `kb-chart` din `shared/ui/` peste `<canvas>`: `chart.js` 4 cu doar controller-ele folosite, încărcat cu `@defer`, în niciun fișier pe care îl încarcă `index.html` (UI §7.8). Cum s-a implementat (grupul 18, decis 2026-11-09): perioadele se numără pe ceasul lui `from` și se compară cu ceasul lui `to` (API-ul știe doar offset-urile, nu fusul orar), deci octombrie la București, de la +03:00 la +02:00, rămâne o perioadă; fiecare `periodStart` păstrează offset-ul lui `from`; luna se numără de fiecare dată de la `from` (31 august, 30 septembrie, 31 octombrie). Cu `userId`, `byUser` îl are doar pe el; `byUser` merge după număr descrescător, apoi nume, `byProject` după număr, apoi cheie. `averageSeconds` și `medianSeconds` sunt `null` fără niciun task finalizat; mediana unui număr par e media celor două din mijloc, rotunjite la secundă; `perColumn` are toate coloanele de acum ale proiectului, de la stânga, cu `null` pentru una prin care n-a trecut niciun task finalizat; șederile se adună pe task și coloană și se taie la `completed_at`; cycle pornește la prima ieșire din coloana de pe poziția 0 de acum, dacă prima mutare a task-ului a plecat din ea, altfel de la `created_at`; nicio durată nu e sub zero. `overdue` nu numără task-urile din proiecte arhivate, ca alertele și panoul principal. Un task terminat chiar la deadline e la timp. Ordinea verificărilor: forma (400 pe `from`, `to` și `granularity`, care se primește în orice majuscule; ordinea și cele 366 de zile pe `to`), apoi proiectul (404 `Project.NotFound` oricui), apoi `reports.view` din token, apoi 400 pe `projectId` lipsă, apoi `EnsureOwnerAsync` (403 `Project.OwnerRequired`); numele permisiunii doar în log. În Application: `Features/Reports/` cu `IReportFilter`, `ReportPeriods`, `ReportTasks`, `FinishedTaskTimeline`, `ReportDurations`, `ReportAccess` și `ReportErrors`; `ReportsController` pe `api/reports`.

### 5.2 Entitate de bază și audit

```csharp
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7(); // UUID v7: ordonabil în timp, index-friendly
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;
    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}
```

Un `AuditingInterceptor : SaveChangesInterceptor` completează câmpurile de audit. Arhivarea (§5.1) e explicită pe `TaskItem` și `Project` prin `IArchivable { ArchivedAt, ArchivedById }` + global query filter, nu un soft delete generic pe toate entitățile — comentariile și istoricul nu se arhivează separat, ele urmează task-ul.

### 5.3 Configurare EF

- Un fișier `IEntityTypeConfiguration<T>` per entitate în `Infrastructure/Persistence/Configurations/`, aplicate cu `ApplyConfigurationsFromAssembly`. **Fără** data annotations pe entități (Domain rămâne curat).
- `UseNpgsql(...).UseSnakeCaseNamingConvention()` → toate tabelele/coloanele/indexurile în `snake_case` automat.
- Concurență optimistă: `builder.Property<uint>("Version").IsRowVersion()` pe `TaskItem`, `Project`, `BoardColumn` (mutările pe kanban concurente se rezolvă cu 409). Npgsql o mapează pe `xmin`; `UseXminAsConcurrencyToken` nu mai există în Npgsql 10. Separat, `IsConcurrencyToken()` pe `Role.HoldersVersion`, `User.RoleId` și `User.IsActive` (invarianta ultimului `Admin`, §4.4) și pe `RefreshToken.RevokedAt` (rotirea refresh token-ului, §4.5). O schimbare a membrilor unui proiect salvează și proiectul, deci îi verifică `xmin`-ul (invarianta „mereu un owner activ”, §4.4), fără coloană nouă. La fel, o schimbare a asignaților nu atinge nicio coloană din `task_items`, așa că `AuditingInterceptor` salvează și task-ul (se mută și `updated_at`, „Actualizat” din Listă): a doua din două schimbări simultane primește 409 `Task.ChangedMeanwhile`, iar două care adaugă aceeași persoană se întâlnesc întâi pe cheia primară a `task_assignees`, cu același 409 (decis 2026-11-07). Efect secundar: o schimbare de membri simultană cu altă schimbare a proiectului, de pildă alocarea unui număr de task din `NextTaskNumber`, primește și ea 409 `Project.ChangedMeanwhile`, pentru că a pornit de la o citire veche. Alocarea numărului de task nu trece prin `xmin`: e un `UPDATE … RETURNING` atomic (§5.1), deci nu se ciocnește nici cu membrii, nici cu altă creare (decis 2026-11-06).
- Crearea unui task e singura salvare dintr-o tranzacție deschisă explicit (`ITaskNumbers`, §4.2). `DomainEventsInterceptor` ține evenimentele unei salvări făcute într-o tranzacție deschisă până la prima salvare de după commit, pe care `TaskNumbers` o face imediat, goală, ca nimeni să nu afle de un task necomis (decis 2026-11-07).
- Enum-uri stocate ca `text` (`HasConversion<string>()`) — lizibile în DB și în Seq, sigure la reordonare.
- Toate datele în `timestamptz` (`DateTimeOffset` în C#). Serverul lucrează în UTC; frontend-ul afișează local.
- `string` → `varchar(n)` explicit cu `HasMaxLength`; `Description` și `Comment.Body` → `text`.
- Migrări în `Infrastructure/Persistence/Migrations/`, nume descriptive: `20261104120000_AddTaskHistory`. Generate cu `dotnet ef migrations add <Name> -p src/Kanabol.Infrastructure -s src/Kanabol.Api`.
- Aplicare: în dev și prod la pornire (`Database:MigrateOnStartup=true`), suficient pentru o singură instanță. Dacă apar mai multe instanțe, trecem la `dotnet ef migrations bundle` rulat ca job separat.
- Seed: un `Admin` inițial (parolă din config/env la prima pornire, forțat schimbată la primul login) + permisiunile din constante, sincronizate la pornire (permisiunile noi din cod apar în DB automat).

---

## 6. Frontend — arhitectură

### 6.1 Structură

```
frontend/src/app/
├── app.config.ts            # providers: router, http (interceptors), animations, localize
├── app.routes.ts            # rute lazy per feature
├── app.ts / app.html        # shell
├── core/                    # o singură instanță, importat doar în app.config
│   ├── auth/                # AuthStore, auth-interceptor, auth-guard, permission-guard
│   ├── api/                 # ApiClient (base URL), error-interceptor, correlation-interceptor
│   ├── realtime/            # SignalR connection service
│   ├── notifications/       # NotificationsApi, modele, notificationText, kb-notification-line, BellStore, kb-notification-bell: navbar-ul nu importă un feature (grupul 17)
│   ├── logging/             # Logger, GlobalErrorHandler
│   └── layout/              # shell, navbar, sidenav
├── shared/                  # reutilizabil, fără stare; section.ts: Section, o parte a unei pagini citită separat, a panoului și a rapoartelor (grupul 18)
│   ├── ui/                  # componente prezentaționale generice (confirm-dialog, empty-state, stat-tile, chart, ...)
│   ├── pipes/
│   ├── directives/          # hasPermission
│   └── models/              # tipuri comune (Page<T>, ProblemDetails)
└── features/
    ├── auth/                # login, change-password
    ├── users/
    ├── roles/
    ├── projects/
    ├── board/               # kanban: board-page, column, task-card, cdk drag & drop
    ├── tasks/               # task-detail, comments, attachments, history, my-tasks-page, my-tasks-store și task-table, tabelul comun al listei și al „Task-urilor mele” (grupul 17a)
    ├── notifications/      # doar pagina, store-ul ei și rutele (grupul 17)
    ├── dashboard/           # panoul principal (grupul 17a)
    └── reports/             # pagina de rapoarte (report-filters, durations, report-csv), încărcată și de ruta „Rapoarte” a proiectului (grupul 18)
```

Fiecare feature: `<feature>.routes.ts`, `<feature>-api.ts` (HTTP), `<feature>-store.ts` (state cu signals), `models.ts`, componente. Feature-urile nu se importă între ele direct; ce e comun urcă în `shared/`. Singura excepție e la nivel de rută: `projects.routes.ts` încarcă leneș (`loadComponent`) pagina de rapoarte din `features/reports/` pentru tab-ul „Rapoarte” al proiectului, cu proiectul fixat; componenta rămâne în chunk-ul ei, nu în bundle-ul inițial (decis 2026-11-09). Paginile placeholder (`PlaceholderPage` din `core/layout/pages.ts`, `features/projects/project-placeholder.ts`) au dispărut odată cu ultimele rute reale (grupul 18).

### 6.2 Pattern-uri folosite în frontend

| Pattern | Cum |
|---|---|
| **Smart / presentational** | `*-page` (container) injectează store-ul și pasează date prin `input()`/`output()`; componentele de sub nu injectează servicii de date |
| **Store per feature cu Signals** | `@Injectable({ providedIn: 'root' }) class BoardStore` cu `signal` privat + `computed` public + metode (`load`, `moveTask`). Fără NgRx |
| **Resource API pentru citiri** | `resource()` / `httpResource()` pentru încărcări dependente de un parametru (ex. board-ul proiectului curent); stare `loading/error` gratis |
| **Optimistic update** | Mutarea unui card actualizează semnalul imediat, apoi apelează API; la eroare face rollback și arată snackbar |
| **Facade API** | `*-api.ts` este singurul loc cu `HttpClient` pentru feature; întoarce `Observable<Dto>` tipat. `HttpClient` merge prin XHR (`withXhr()` în `app.config.ts`), nu prin fetch, ca upload-ul să-și raporteze progresul (§10; decis 2026-11-08) |
| **Interceptori funcționali** | `authInterceptor` (Bearer + refresh la 401, o singură dată, cu coadă; refresh-ul e serializat și între taburi cu `navigator.locks`, altfel două taburi pornite odată declanșează detecția de refolosire din §4.5), `correlationInterceptor` (`X-Correlation-Id`), `errorInterceptor` (ProblemDetails → mesaj) |
| **Route guards funcționali** | `authGuard`, `permissionGuard('projects.view_all')` |
| **Directivă `*kbHasPermission`** | Ascunde elemente din UI; **nu** e securitate, backend-ul verifică oricum |
| **`@defer`** pentru widget-uri grele (rapoarte, editor de descriere). Primul `@defer` din aplicație (graficele rapoartelor, grupul 18) a adus runtime-ul lui în bundle-ul inițial, 8,85 kB, acceptat (decis 2026-11-09). Bundle-ul inițial e un task al grupului 19: măsurat cu `stats-json`, ce nu e nevoie la pornire mutat în chunk-uri (Material neimportat de shell, `@defer` sau `import()` la `kb-chart`), înainte și după în raport, bugetul din `angular.json` la ce rămâne plus 5%, rotunjit la 10 kB, eroarea la 1 MB, ținta orientativă sub 800 kB, fără schimbări vizibile (decis 2026-11-09). Cum s-a implementat (grupul 19): „Initial total” 893,03 → 706,01 kB (transfer 203,07 → 174,17 kB), 706,14 cu token-urile graficelor; au plecat paginatorul, cu `mat-select`, `mat-form-field` și `@angular/forms` după el (~117 kB), pentru că `app.config.ts` dădea `MatPaginatorIntl` (acum `providePaginatorLabels()` în fiecare componentă cu paginator), și slide toggle-ul (~70 kB), pentru că `ThemeToggle` era în navbar cu opțiunile din `app.config.ts` (acum și le dă singur, iar navbar-ul îl încarcă cu `@defer (on idle)`); rămân ale shell-ului sidenav, meniu, butoane, tooltip, snackbar, overlay-ul și dialogul (~32 kB, `MatDialog` e în injectorul rădăcină cu `MAT_DIALOG_DEFAULT_OPTIONS`); `kb-chart` rămâne cu `@defer`, runtime-ul lui fiind oricum în bundle pentru comutatorul temei. Bugetul: 706 + 5 % = 741, rotunjit în sus la 750 kB, eroarea la 1 MB |
| **OnPush peste tot** | Cu signals nu e nevoie de `ChangeDetectorRef`; setat ca default în `angular.json` → `schematics` |
| **Zoneless** | Default în Angular 21+ pentru proiecte noi; repo-ul e deja fără `zone.js`. Nu adăugăm niciodată `zone.js` înapoi |
| **`provideBrowserGlobalErrorListeners()`** | Deja în `app.config.ts`; `GlobalErrorHandler` (§8.3) se înregistrează ca `ErrorHandler` lângă el |

### 6.3 Kanban

`@angular/cdk/drag-drop`: `cdkDropListGroup` pe board, `cdkDropList` pe coloană, `cdkDrag` pe card. La `drop` → `BoardStore.moveTask(taskId, targetColumnId, index)` → `PATCH /api/tasks/{id}/position`. SignalR primește `TaskMoved` de la alți utilizatori și aplică în store (ignorat dacă provine de la propriul `connectionId`).

### 6.4 i18n

`@angular/localize`, locale sursă `ro`, texte în template cu atributul `i18n`. Extragere cu `ng extract-i18n`. Un singur build `ro` în v1; `en` se adaugă prin fișier `messages.en.xlf` fără schimbări de cod. Datele/numerele formatate cu pipe-uri Angular în `ro-RO`.

---

## 7. Convenții de naming

### 7.1 C#

| Element | Convenție | Exemplu |
|---|---|---|
| Namespace | `Kanabol.<Layer>.<Folder>` (file-scoped) | `Kanabol.Application.Features.Tasks.Commands.MoveTask` |
| Clase, records, enum, metode, proprietăți | PascalCase | `TaskItem`, `MoveTo()` |
| Interfețe | `I` + PascalCase | `IAppDbContext` |
| Câmpuri private | `_camelCase` | `_domainEvents` |
| Parametri, locale | camelCase | `targetColumnId` |
| Constante | PascalCase | `Permissions.ProjectsViewAll` |
| Metode async | sufix `Async` doar în interfețe de infrastructură; handlerele au `Handle` | `SaveChangesAsync`, `Handle` |
| Commands / Queries | `<Verb><Noun>Command` / `Get<Noun>[By…]Query` | `CreateTaskCommand`, `GetBoardQuery` |
| Handlere | `<Command>Handler` | `CreateTaskCommandHandler` |
| Validatoare | `<Command>Validator` | `CreateTaskCommandValidator` |
| DTO-uri (răspuns) | `<Noun>Dto`, `<Noun>SummaryDto` | `TaskItemDto` |
| Request body API | `<Verb><Noun>Request` | `CreateTaskRequest` |
| Domain events | `<Noun><PastTenseVerb>Event` | `TaskMovedEvent` |
| Erori | clasă statică `<Noun>Errors` | `TaskErrors.NotFound(id)` |
| Configurări EF | `<Entity>Configuration` | `TaskItemConfiguration` |
| Teste | `<Metodă>_<Scenariu>_<Rezultat>` | `Handle_WhenUserIsNotMember_ReturnsForbidden` |
| Fișiere | un tip public per fișier, numele fișierului = numele tipului | |

### 7.2 Bază de date (PostgreSQL)

| Element | Convenție | Exemplu |
|---|---|---|
| Tabele | `snake_case`, plural | `task_items`, `project_members` |
| Coloane | `snake_case` | `due_date`, `created_at` |
| PK | `id` (uuid v7) | |
| FK | `<entitate>_id` | `project_id`, `assigned_by_id` |
| Indexuri | `ix_<tabel>_<coloane>` (generat de EF) | `ix_task_items_project_id_column_id` |
| Unique | `ux_<tabel>_<coloane>` | `ux_users_email`, `ux_roles_lower_name` (pe `lower(name)`, creat cu SQL în migrare), `ux_projects_key`, `ux_task_items_project_id_number` |
| FK constraint | `fk_<tabel>_<tabel_referit>_<coloană>` (generat) | |
| Timestamp-uri | `timestamptz`, sufix `_at` | `completed_at` |
| Booleans | prefix `is_`/`has_` | `is_done`, `is_active` |
| Enum-uri | `text` | `priority = 'High'` |
| Join tables | numele ambelor entități | `role_permissions`, `project_members`, `task_assignees` |
| Arhivare | `archived_at`, `archived_by_id` | pe `task_items`, `projects` |

### 7.3 API REST

| Element | Convenție | Exemplu |
|---|---|---|
| Base path | `/api` (fără versiune în v1; `/api/v2` doar dacă e nevoie) | |
| Resurse | plural, kebab-case | `/api/projects/{id}/board-columns` |
| Acțiuni non-CRUD | sub-resursă sau verb explicit ca ultimul segment | `PATCH /api/tasks/{id}/position`, `POST /api/auth/refresh` |
| JSON | camelCase (default System.Text.Json) | `dueDate` |
| Paginare | `?page=1&pageSize=50` → `Page<T> { items, page, pageSize, totalCount }` | |
| Sortare / filtrare | `?sort=dueDate:desc&assigneeId=…` | |
| Coduri | 200 citire/update, 201 + `Location` creare, 204 ștergere, 4xx ProblemDetails | |
| SignalR hubs | `/hubs/board` | |
| Headers | `X-Correlation-Id` (in/out) | |

### 7.4 Angular / TypeScript

Urmăm style guide-ul Angular v20+ (fără sufixe `.component`/`.service` în numele fișierelor), cu descriptori acolo unde clarifică rolul:

| Element | Fișier | Clasă / simbol |
|---|---|---|
| Componentă | `task-card.ts` / `.html` / `.scss` | `TaskCard`, selector `kb-task-card` |
| Pagină (smart) | `board-page.ts` | `BoardPage` |
| Store | `board-store.ts` | `BoardStore` |
| API service | `tasks-api.ts` | `TasksApi` |
| Alte servicii | `logger.ts`, `realtime.ts` | `Logger`, `Realtime` |
| Guard / interceptor | `auth-guard.ts`, `auth-interceptor.ts` | `authGuard`, `authInterceptor` |
| Directive | `has-permission.ts` | `HasPermission`, selector `[kbHasPermission]` |
| Pipe | `relative-time-pipe.ts` | `RelativeTimePipe`, name `relativeTime` |
| Modele | `models.ts` per feature | `interface TaskItemDto` (oglindesc DTO-urile din API) |
| Rute | `<feature>.routes.ts` | `export const BOARD_ROUTES: Routes` |
| Teste | `task-card.spec.ts` | `describe('TaskCard')` |
| Variabile / funcții | camelCase; signals fără prefix/sufix | `tasks = signal<TaskItemDto[]>([])` |
| Constante globale | UPPER_SNAKE | `API_BASE_URL` |
| Inputs / outputs | `input()`, `output()` funcționale; outputs fără prefix `on` | `moved = output<MoveEvent>()` |
| Prefix selector | `kb` | |

### 7.5 Git

- Branch-uri: `feature/<kebab>`, `fix/<kebab>`, `chore/<kebab>`, `docs/<kebab>`.
- Commits: **Conventional Commits**, în engleză: `feat(board): drag and drop between columns`. Scopes: `api`, `web`, `db`, `auth`, `board`, `tasks`, `infra`, `ci`, `docs`. Verificat de `commitlint` în hook-ul `commit-msg`.
- Tag-uri de release: `v0.1.0` (SemVer).
- Mesajele de commit și PR-urile **nu menționează Claude** și nu conțin `Co-Authored-By` sau altă atribuire AI (regulă din `CLAUDE.md`).

---

## 8. Loguri

### 8.1 Backend — Serilog

- **Structurate**: message templates cu proprietăți numite, niciodată interpolare (analizorul CA2254 forțează asta). `Log.Information("Task {TaskId} moved to column {ColumnId} by {UserId}", …)`.
- **Destinații**: consolă (format lizibil în dev, JSON compact în prod — Docker le strânge) + **Seq** (`http://seq:5341`). Seq ține 30 de zile (setat o dată per volum, cu `scripts/seq-retention.sh`; retenția stă în volum).
- **Enrichers**: `FromLogContext`, `WithEnvironmentName`, `WithMachineName`, plus proprii: `CorrelationId`, `UserId`, `RequestPath`.
- **Request logging**: `app.UseSerilogRequestLogging()` — o linie per request cu metodă, path, status, durată. Înlocuiește logurile verbose ASP.NET.
- **Niveluri**:

| Sursă | Dev | Prod |
|---|---|---|
| Default | Debug | Information |
| `Microsoft.AspNetCore` | Information | Warning |
| `Microsoft.EntityFrameworkCore.Database.Command` | Information (vezi SQL-ul) | Warning |
| `Kanabol.*` | Debug | Information |

- **Ce logăm**: fiecare command executat (nume, durata, succes/eșec — din `LoggingDecorator`), autentificări reușite/eșuate (cu `UserId`/email, fără parolă), permisiuni refuzate, job-urile de alerte (câte task-uri procesate), erori neașteptate cu stack trace.
- **Ce NU logăm**: parole, token-uri (nici hash-ul), conținutul atașamentelor, body-ul complet al request-urilor. Dacă e nevoie de debugging pe body, se activează explicit și temporar în dev. Token-ul din query string-ul hub-ului ar ajunge în „Request starting” al ASP.NET (Development scrie adresa întreagă) și în orice intrare cu adresa: `AccessTokenRedaction`, un enricher Serilog, ascunde valoarea lui `access_token` în orice proprietate a unei intrări, iar logul clientului SignalR trece prin `Logger` cu token-ul ascuns; nginx nu ține access log pe `/hubs/` (§13.2), error log-ul lui și redirect-ul de pe portul 80 rămân cum sunt. Redactarea și în valorile structurate (liste, obiecte, dicționare, `Uri` ca text), recursiv, cu teste pe fiecare, e un task al grupului 19 (decis 2026-11-07, detaliat 2026-11-09). Cum s-a implementat (grupul 19): un `Uri` devine text doar când are token, cheile dicționarelor se redactează și ele, o valoare fără token rămâne aceeași instanță; cum nimic din ce primește API-ul nu ajunge azi în log ca valoare structurată, acelea se verifică prin pipeline-ul Serilog al API-ului în teste, iar pe API-ul real doar adresa ca text.
- **Loguri ≠ audit**: istoricul task-urilor stă în DB (`task_history_entries`), e feature de produs. Seq e pentru operare/debugging.

```json
// appsettings.json (fragment)
"Serilog": {
  "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.Seq"],
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "Enrich": ["FromLogContext", "WithEnvironmentName", "WithMachineName"],
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "Seq", "Args": { "serverUrl": "http://seq:5341" } }
  ]
}
```

### 8.2 Correlation ID

Middleware `CorrelationIdMiddleware` (primul în pipeline): citește `X-Correlation-Id` din request sau generează un GUID, îl pune în `HttpContext.Items`, în `LogContext` și în header-ul de răspuns. Frontend-ul generează unul per request (`crypto.randomUUID()`) și îl afișează în mesajele de eroare („Cod eroare: …”) ca utilizatorul să-l poată raporta. SignalR: `connectionId` logat ca proprietate.

### 8.3 Frontend

- `Logger` (`core/logging`): `debug/info/warn/error`; în dev scrie în consolă; în prod `warn`+`error` se trimit la `POST /api/client-logs` (batch, cu `correlationId`, URL, user agent) ca să apară în Seq alături de cele de backend.
- Limita de 10 intrări pe minut e în browser: ce trece peste ea se numără și vine ca `dropped` în batch-ul următor, pe care serverul îl loghează ca warning. Serverul nu numără cererile pe minut; primește cel mult 10 intrări într-un batch, doar `warn` și `error`, cu lungimi limitate (mesaj 4.000, URL 2.000, detalii 16.000, correlation id 100 de caractere) și răspunde 204. O limită pe adresă la server se adaugă doar dacă apare nevoia (decis 2026-11-06).
- Trimite doar un utilizator autentificat, ca orice controller, și nu cât timp `MustChangePassword` e activ: erorile din pagina de login și din schimbarea obligatorie a parolei rămân în consola browserului, nu ajung în Seq. Browserul nici nu trimite fără sesiune, ca un 401 să nu-l ducă la login (decis 2026-11-06).
- `GlobalErrorHandler` (`ErrorHandler` Angular): prinde erorile neprinse → `Logger.error` + snackbar generic.
- Fără `console.log` în cod comis (regulă ESLint `no-console: ["error", { allow: ["warn", "error"] }]`; Logger-ul are excepție).

---

## 9. Real-time (SignalR)

- Hub `BoardHub` la `/hubs/board`, autorizat cu JWT, doar peste WebSockets: la long polling și la Server-Sent Events fiecare cerere poate aduce un token proaspăt și muta expirarea, deci cine era scos dintr-un proiect l-ar auzi fără sfârșit (decis 2026-11-07). Clientul apelează `JoinProject(projectId)` → grup `project:{id}`; serverul verifică accesul prin `IProjectAccess` (membru, `projects.view_all` sau `projects.manage`, cont activ; un proiect inexistent nu e căutat), iar `LeaveProject(projectId)` îl scoate din grup. Un refuz e răspunsul cu codul `Project.MemberRequired`, nu o excepție, ca SignalR să nu-l scrie ca `Error`, și un warning în log; join-urile și refuzurile se loghează cu `UserId`, `ProjectId` și `ConnectionId` (§8.2). Cine a pierdut accesul la un proiect, sau contul, mai aude id-urile schimbărilor cel mult până îi expiră token-ul (15 minute, §4.5); o evidență utilizator → conexiuni, care îl scoate din grupuri la scoaterea din proiect, la schimbarea rolului și la dezactivare, e un task al grupului 19 (decis 2026-11-07).
- Evenimente către client: `TaskCreated`, `TaskMoved`, `TaskUpdated`, `TaskArchived`, `TaskRestored`, `CommentAdded` (grup `project:{id}`), `NotificationCreated` (grup `user:{id}`, payload `{ notificationId }`, pentru oricine a primit notificarea, oricine a făcut-o; conexiunea intră automat în grupul propriu; alimentează clopoțelul din navbar; decis 2026-11-07).
- Sursa: domain events → `IDomainEventHandler<TaskMovedEvent>` în Application → `IBoardNotifier` (interfață în Application, implementată în Api peste `IHubContext<BoardHub>`). Handlerele de domeniu nu știu de SignalR. `TaskAssignedEvent` trimite și el `TaskUpdated`, cu `field = Assignees`, pentru că schimbarea asignaților nu ridică `TaskUpdatedEvent` (decis 2026-11-07); `CommentAdded` vine din `CommentAddedEvent` (grupul 15), cu `commentId` pe lângă câmpurile comune, iar un atașament urcat sau șters trimite `TaskUpdated` cu `field = Attachments`, pentru numărul de pe card; board-ul se recitește în liniște la amândouă, iar panoul deschis nu ascultă nici acum (decis 2026-11-07).
- Payload-uri mici (decis 2026-11-07): fiecare eveniment poartă `taskId`, `projectId`, `byUserId` și `originConnectionId`; `TaskMoved` și `columnId` și `position`, ca board-ul să mute cardul local cu animația din UI §7.4; `TaskUpdated` și `field` (`Title`, `Description`, `Priority`, `DueDate`, `Assignees` sau `Attachments`). La `TaskCreated`, `TaskUpdated`, `TaskArchived` și `TaskRestored` board-ul se recitește în liniște, cu un `GET /api/projects/{id}/board`, și pulsează cardul schimbat; la reconectare face re-join și se recitește, pentru ce a pierdut. O creare cu asignați trimite `TaskCreated` și `TaskUpdated` cu `Assignees`, deci celelalte board-uri se recitesc de două ori, acceptat (decis 2026-11-07). În v1 ascultă doar board-ul, nu Lista și nu panoul.
- Propria conexiune (decis 2026-11-07): clientul pune id-ul conexiunii lui în header-ul `X-Connection-Id` pe fiecare cerere API; un context de cerere scoped în Application, lângă `ICurrentUser`, îl dă handlerelor, care îl pun în payload ca `originConnectionId`, iar `BoardStore` ignoră un eveniment doar când `originConnectionId` e al tab-ului lui și `byUserId` e utilizatorul autentificat: id-urile conexiunilor se văd în payload-uri de toți membrii proiectului, deci un id luat de la altul nu ascunde nimic. Header-ul trece doar ca id de SignalR (litere, cifre, `-` și `_`, cel mult 64 de caractere, trimis o dată), altfel e nul. Fără conexiune header-ul lipsește, `originConnectionId` e nul și nimeni nu ignoră nimic (decis 2026-11-07).
- Client: `Realtime` service (`core/realtime`) cu `HubConnectionBuilder`, reconectare automată, re-join la reconectare; store-urile se abonează. Conexiunea se deschide la primul board deschis și ține cât sesiunea; SignalR stă în chunk-ul board-ului (`TabConnection` ține id-ul pentru interceptor), ca bundle-ul inițial să nu crească. Clopoțelul (grupul 17) cheamă `Realtime` după autentificare, ca grupul propriu să existe și fără board deschis, printr-un `import()` dinamic al modulului (`Realtime.open()`, conexiunea fără proiect; grupul 17), ca SignalR să rămână în afara bundle-ului inițial, în chunk-ul pe care îl împarte cu board-ul; la `NotificationCreated` și la reconectare clopoțelul recitește numărul necitit și, dacă meniul e deschis, lista; la ieșirea din cont uită tot (decis 2026-11-07, importul dinamic 2026-11-08).
- Limite cunoscute, de rezolvat în grupul 19 (decis 2026-11-07): `JoinProject` se poate chema oricât, fiecare refuz e o citire din bază și un warning, iar `projects.view_all` și `projects.manage` intră în grupuri pentru orice id, deci urmează cel mult câteva proiecte pe conexiune și închiderea conexiunii după câteva refuzuri; trimiterile se fac în cererea care a făcut schimbarea, deci un client care nu mai citește din socket ține fiecare salvare din proiect până la `TransportSendTimeout` (10 s), iar urmează un canal în fundal cu un singur consumator, ca ordinea să rămână. Decis 2026-11-09, pentru grupul 19: cel mult 8 proiecte pe conexiune (`JoinProject` răspunde `Hub.TooManyProjects` fără citire din bază; `LeaveProject` eliberează locul); după 5 refuzuri pe o conexiune, `Context.Abort()` cu un warning; o evidență în memorie în Api (un singur API, §4.3) utilizator → conexiuni → proiectele intrate, și o interfață `IRealtimeAccess` în Application (`RemoveFromProject`, `Disconnect`) chemată din handler-e: scoaterea din proiect scoate conexiunile omului din grupul proiectului, schimbarea rolului și dezactivarea îi închid conexiunile (se reconectează cu token nou, sau deloc). Trimiterile: `HubBoardNotifier` scrie într-un `Channel` nelimitat, iar un hosted service cu un singur consumator trimite în ordine, prinde și loghează erorile, deci cererea care a făcut schimbarea nu mai așteaptă socket-ul nimănui; la oprire golește ce a rămas, cel mult 5 s. Cum s-a implementat (grupul 19, decis 2026-11-09): `IRealtimeAccess` are `RemoveFromProjectAsync` și `DisconnectAsync`; evidența e `RealtimeConnections` în Api (singleton), care implementează și `IRealtimeAccess`; `Hub.TooManyProjects` stă în `HubErrors` din Api; un proiect în care conexiunea e deja intrată se reia fără citire din bază și fără loc nou; ambele refuzuri (`Hub.TooManyProjects` și `Project.MemberRequired`) se numără la cele 5; join-urile, ieșirile și scoaterile pe o conexiune merg pe rând, ca o scoatere venită cât un join citește din bază să-l scoată după; scoaterea din proiect nu-l scoate pe cine vede proiectul printr-o permisiune (`projects.view_all`, `projects.manage`); schimbarea permisiunilor unui rol nu închide nimic (token-ul le ține până la 15 minute, ca la API); `ResetUserPassword` nu închide conexiunile. Canalul e `BoardOutbox` (nelimitat), citit doar de `BoardSender`, hosted service înregistrat înaintea worker-elor, ca să se oprească după ele; la oprire canalul nu mai primește nimic, ce vine după se pierde cu un mesaj Debug, iar ce rămâne netrimis după 5 secunde e un warning cu numărul. În teste, `RealtimeApiTest.Connect` face un apel după `StartAsync`, pentru că SignalR încheie handshake-ul înainte de `OnConnectedAsync`, iar sondele trec prin `IBoardNotifier`, ca să vină după ce a trimis o cerere.

---

## 10. Atașamente

- `IFileStorage { SaveAsync(stream, fileName, ct) → StoredFile(Key, Size, ContentType); OpenReadAsync(key); DeleteAsync(key) }`.
- Implementare v1: `LocalFileStorage` → `/data/attachments/{yyyy}/{MM}/{guid}{ext}` (volum Docker). Metadatele (`FileName`, `ContentType`, `Size`, `StorageKey`, `UploadedById`) în `attachments`.
- Limite: 25 MB/fișier (25 × 1.048.576 de octeți), listă albă de extensii din `Storage:AllowedExtensions` (implicit pdf, png, jpg, jpeg, gif, txt, md, csv, xlsx, docx, zip), numele original nu ajunge niciodată pe disc. Lista e o singură valoare cu virgule, `Storage__AllowedExtensions=pdf,png` în compose, care înlocuiește toată lista implicită; extensiile se iau o dată fiecare, fără spații și fără punct, în litere mici, în ordinea dată. API-ul nu pornește fără nicio extensie, nici cu o listă pe indici (`Storage__AllowedExtensions__0=exe`, forma de dinaintea grupului 16), pe care legarea la o singură valoare ar ignora-o în tăcere; mesajul de la pornire dă forma cea nouă (decis 2026-11-08). Urcă un membru real pe un task deschis, un fișier pe cerere, prin `POST /api/tasks/{id}/attachments`, în câmpul `file`; `DELETE /api/attachments/{id}` șterge rândul și apoi fișierul, pentru cine l-a urcat sau pentru `tasks.archive` (§4.4; decis 2026-11-07).
- Refuzul e 400 pe `file` (decis 2026-11-08): fără fișier, cu două, în alt câmp, cu un nume gol, de peste 255 de caractere sau cu caractere de control, peste limită sau cu o extensie din afara listei. Numele păstrează doar ce vine după ultimul `/` sau `\`; extensia e ce vine după ultimul punct, în orice majuscule (`arhiva.tar.gz` e `gz`). Cererea poate avea cu 1 MB mai mult decât fișierul; peste asta API-ul nu citește formularul și răspunde același 400 (nginx oprește peste 30 MB cu 413, §13.2). Tipul conținutului vine din extensie, nu de la browser. Un fișier rămas pe disc după ce rândul lui a dispărut e un warning în log.
- Download prin `GET /api/attachments/{id}` cu verificarea accesului la task; `Content-Disposition: attachment` cu numele original și `X-Content-Type-Options: nosniff`. Token-ul fiind în memorie, pagina citește fișierul ca blob prin `HttpClient` și îl salvează printr-un link pe care îl apasă ea, nu printr-un `<a href>` simplu; upload-ul își raportează progresul prin XHR (§6.2; decis 2026-11-08).

---

## 11. Teste

### 11.1 Backend

| Proiect | Testează | Cum | Viteză |
|---|---|---|---|
| `Kanabol.Domain.Tests` | Invariante pe entități (`TaskItem.MoveTo` adaugă istoric, `Project` nu rămâne fără owner) | xUnit pur, fără DI | ms |
| `Kanabol.Application.Tests` | Handlere end-to-end pe **Postgres real** | Testcontainers (`PostgreSqlContainer`) într-un `ICollectionFixture` partajat; `Respawn` resetează tabelele între teste; `ICurrentUser`/`IClock`/`IBoardNotifier` cu NSubstitute | ~5-10 s pornire container, apoi ms/test |
| `Kanabol.Api.Tests` | Endpoint-uri: auth, autorizare pe permisiuni, ProblemDetails, SignalR handshake | `WebApplicationFactory<Program>` + același container | idem |

Motiv pentru Postgres real și nu InMemory/SQLite: `xmin`, `timestamptz`, `snake_case` și comportamentul tranzacțiilor diferă; testele pe InMemory dau încredere falsă.

Convenții: `FluentAssertions` (`result.IsSuccess.Should().BeTrue()`), un `TestDataBuilder` per aggregate (`new TaskItemBuilder().InProject(p).AssignedTo(u).Build()`), fără `[Fact]` care depind de ordine. Fișier `tests/Directory.Build.props` cu `IsPackable=false`, `IsTestProject=true`.

Țintă de acoperire: nu impunem procent; impunem că fiecare handler de command are cel puțin un test de succes și unul de refuz de acces.

### 11.2 Frontend

- Vitest prin builder-ul Angular (`@angular/build:unit-test`), `*.spec.ts` lângă fișier, jsdom.
- Store-urile se testează fără TestBed (sunt clase cu signals); API-urile cu `provideHttpClientTesting`; componentele prezentaționale cu `TestBed` + harness-uri Material (`@angular/material/<x>/testing`).
- `ng test` în watch local; `ng test --no-watch` în hook.
- E2E (Playwright): nu în v1; se adaugă când există flux de login + board stabil.

---

## 12. Lint și formatare

### 12.1 Comun — `.editorconfig` (rădăcina repo)

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 2

[*.cs]
indent_size = 4
csharp_style_namespace_declarations = file_scoped:error
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_prefer_braces = true:error
csharp_using_directive_placement = outside_namespace:error
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:error
dotnet_naming_rule.private_fields_underscore.severity = error
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case
dotnet_diagnostic.CA2254.severity = error   # template-uri de log, nu interpolare
dotnet_diagnostic.CA1848.severity = none    # LoggerMessage delegates: prea mult zgomot
dotnet_diagnostic.CA1062.severity = none    # null checks pe argumente: Nullable le acoperă

[*.{ts,html,scss,json,yaml,yml,md}]
indent_size = 2
```

### 12.2 Backend — `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

Verificare: `dotnet format --verify-no-changes` (stil) + `dotnet build -warnaserror` (analizoare). Fără StyleCop — analizoarele .NET + `.editorconfig` sunt suficiente și nu necesită pachet extra. În `Kanabol.Domain` și `Kanabol.Application` adăugăm regulă de arhitectură prin test (`NetArchTest.Rules`): `Domain` nu referă `Microsoft.EntityFrameworkCore`; `Application` nu referă `Infrastructure`.

### 12.3 Frontend — ESLint + Prettier

`eslint.config.js` (flat config):

```js
// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const prettier = require('eslint-config-prettier');

module.exports = tseslint.config(
  { ignores: ['dist/**', '.angular/**', 'coverage/**'] },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.strictTypeChecked,
      ...tseslint.configs.stylisticTypeChecked,
      ...angular.configs.tsRecommended,
      prettier,
    ],
    languageOptions: { parserOptions: { projectService: true, tsconfigRootDir: __dirname } },
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'kb', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'kb', style: 'kebab-case' }],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@angular-eslint/prefer-signals': 'error',
      '@angular-eslint/prefer-standalone': 'error',
      '@typescript-eslint/explicit-member-accessibility': ['error', { accessibility: 'no-public' }],
      '@typescript-eslint/no-floating-promises': 'error',
      'no-console': ['error', { allow: ['warn', 'error'] }],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: { '@angular-eslint/template/prefer-control-flow': 'error' },
  },
  {
    files: ['**/*.spec.ts'],
    rules: { '@typescript-eslint/no-non-null-assertion': 'off', '@typescript-eslint/unbound-method': 'off' },
  },
);
```

`.prettierrc`: `{ "printWidth": 100, "singleQuote": true, "trailingComma": "all", "overrides": [{ "files": "*.html", "options": { "parser": "angular" } }] }`.

`tsconfig.json`: `strict: true`, `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`, `angularCompilerOptions.strictTemplates: true`.

---

## 13. Docker

### 13.1 `backend/Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props Kanabol.sln ./
COPY src/Kanabol.Domain/*.csproj src/Kanabol.Domain/
COPY src/Kanabol.Application/*.csproj src/Kanabol.Application/
COPY src/Kanabol.Infrastructure/*.csproj src/Kanabol.Infrastructure/
COPY src/Kanabol.Api/*.csproj src/Kanabol.Api/
RUN dotnet restore src/Kanabol.Api/Kanabol.Api.csproj
COPY src/ src/
# The analyzers run in the commit hooks, with the rules in the root .editorconfig. That file is
# outside this build context, so here they would report rules the project has turned off.
RUN dotnet publish src/Kanabol.Api/Kanabol.Api.csproj -c Release -o /app --no-restore -p:RunAnalyzers=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The image has neither wget nor curl, and the healthcheck needs one of them.
RUN apt-get update \
  && apt-get install -y --no-install-recommends wget \
  && rm -rf /var/lib/apt/lists/* \
  && mkdir -p /data/attachments \
  && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app .
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Kanabol.Api.dll"]
```

Restore separat de sursă → cache-ul Docker nu reface restore la fiecare schimbare de cod. Testele **nu** rulează în Dockerfile (rulează în hook); imaginea e doar runtime. Tot în hook rulează și analizoarele: la publish sunt oprite, pentru că regulile lor stau în `.editorconfig` din rădăcină, în afara contextului de build. Imaginea `aspnet` n-are nici `wget`, nici `curl`, așa că `wget` se instalează pentru healthcheck.

### 13.2 `frontend/Dockerfile`

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-app.conf /etc/nginx/kanabol-app.conf
COPY --from=build /app/dist/kanabol/browser /usr/share/nginx/html
EXPOSE 80
```

nginx are trei fișiere. Ce au comun dev și prod stă în `nginx-app.conf`, inclus din `nginx.conf` (dev) și din `nginx.prod.conf`, pe care `compose.prod.yaml` îl montează peste `nginx.conf`. Headerele (grupul 19, decis 2026-11-09) stau la nivel de server în `nginx-app.conf`, nu în `location`-uri, unde `add_header` le-ar pierde pe cele de sus: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` fără cameră, microfon și geolocation, și CSP `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'` (Angular și Material injectează stiluri inline; un nonce ar cere `sub_filter` în nginx și rămâne pentru mai târziu); HSTS `max-age=31536000`, fără preload, doar în `nginx.prod.conf`, pe 443. `check-docker.sh` le verifică pe stack-ul real. Cum s-a implementat (grupul 19, decis 2026-11-09): `always` pe fiecare `add_header`, ca și paginile de eroare ale lui nginx (413) și 4xx-urile API-ului să le aibă; build-ul de producție are `optimization.styles.inlineCritical: false` în `angular.json`, pentru că inlinierea CSS-ului critic punea stylesheet-ul pe `media="print"` și îl comuta un `<script>` inline, pe care `script-src 'self'` îl blochează, iar aplicația rămânea fără stiluri; acum stylesheet-ul e un `<link>` obișnuit. Verificat pe stack-ul Docker cu API-ul real în browser: login, board cu WebSocket, rapoarte cu grafice, nicio încălcare de CSP.

`nginx-app.conf`:

```nginx
# What the development and production servers have in common. They include it from
# nginx.conf and nginx.prod.conf.
root /usr/share/nginx/html;
index index.html;

location /api/ {
  proxy_pass http://api:8080;
  # Without Host, a 201's Location would point to http://api:8080.
  proxy_set_header Host $host;
  proxy_set_header X-Forwarded-For $remote_addr;
  # nginx allows 1 MB by default. Uploads go up to 25 MB, and the API is the one to refuse more.
  client_max_body_size 30m;
}
location /hubs/ {
  proxy_pass http://api:8080;
  proxy_http_version 1.1;
  proxy_set_header Host $host;
  # Here too: the API takes the client address from this header, so no request may bring its own.
  proxy_set_header X-Forwarded-For $remote_addr;
  proxy_set_header Upgrade $http_upgrade;
  proxy_set_header Connection "upgrade";
  proxy_read_timeout 100s;
  # The browser sends its access token in the query string here, which the access log would keep
  # (docs/SPEC.md §8.1). The API logs these requests itself, without the query.
  access_log off;
}
location / { try_files $uri $uri/ /index.html; }
# nginx doesn't know this extension, and would send it as a binary download.
location = /manifest.webmanifest { default_type application/manifest+json; }

gzip on;
gzip_types text/css application/javascript application/json image/svg+xml;
add_header X-Content-Type-Options nosniff always;
add_header X-Frame-Options DENY always;
# The other headers of section 13.2 follow here, each with always (group 19).
```

`nginx.conf`:

```nginx
server {
  listen 80;
  include /etc/nginx/kanabol-app.conf;
}
```

`nginx.prod.conf`:

```nginx
# compose.prod.yaml mounts this over nginx.conf. The certificates come from scripts/make-certs.sh.
server {
  listen 80;
  return 301 https://$host$request_uri;
}

server {
  listen 443 ssl;
  ssl_certificate /etc/nginx/certs/server.crt;
  ssl_certificate_key /etc/nginx/certs/server.key;
  include /etc/nginx/kanabol-app.conf;
}
```

Același origin pentru SPA și API (prin nginx) → fără CORS în prod, cookie-ul de refresh funcționează cu `SameSite=Strict`.

### 13.3 Compose

`compose.yaml` (comun):

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: kanabol
      POSTGRES_USER: kanabol
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U kanabol -d kanabol"]
      interval: 5s
      timeout: 3s
      retries: 10

  seq:
    image: datalust/seq:latest
    environment:
      ACCEPT_EULA: "Y"
      SEQ_FIRSTRUN_ADMINPASSWORDHASH: ${SEQ_ADMIN_HASH}
    volumes: [seqdata:/data]

  api:
    build: ./backend
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}
      ConnectionStrings__Default: Host=postgres;Database=kanabol;Username=kanabol;Password=${POSTGRES_PASSWORD}
      Jwt__Secret: ${JWT_SECRET}
      # Needed only when the database is new: that first start creates the admin user.
      Admin__InitialPassword: ${ADMIN_INITIAL_PASSWORD}
      Serilog__WriteTo__1__Args__serverUrl: http://seq:5341
      Storage__Root: /data/attachments
      # The allowed extensions have a default in appsettings.json; override the whole list here as Storage__AllowedExtensions=pdf,png (one value, commas; group 16)
      # The overdue check runs every 15 minutes by default: Alerts__OverdueCheckInterval=00:05:00 changes it, Alerts__OverdueCheckEnabled=false stops it (group 17)
      # The refresh token cleanup runs once a day by default: Auth__RefreshTokenCleanupInterval=01:00:00 changes it (1 hour to 30 days), Auth__RefreshTokenCleanupEnabled=false stops it (group 19)
      Database__MigrateOnStartup: "true"
    volumes: [attachments:/data/attachments]
    depends_on:
      postgres: { condition: service_healthy }
      seq: { condition: service_started }

  web:
    build: ./frontend
    depends_on: [api]

volumes:
  pgdata:
  seqdata:
  attachments:
```

`compose.override.yaml` (dev, încărcat automat de `docker compose up`):

```yaml
# Development settings, which docker compose loads by itself. Usually only the first two services
# run here, with the API and Angular on the host: docker compose up postgres seq
services:
  postgres:
    ports: ["127.0.0.1:5432:5432"]

  seq:
    # Inside the container, 80 serves the UI and takes logs, while 5341 only takes logs.
    # So both the UI and the local API's logs use localhost:5341.
    ports: ["127.0.0.1:5341:80"]

  api:
    ports: ["127.0.0.1:8080:8080"]

  web:
    ports: ["127.0.0.1:4200:80"]
```

Porturile sunt legate de `127.0.0.1`, deci rămân pe mașina dezvoltatorului. În dev obișnuit **nu** pornim `api` și `web` din Docker: rulăm `docker compose up postgres seq`, apoi `dotnet watch --launch-profile http` (API pe `http://localhost:5200`, profilul existent) și `ng serve` local (hot reload, debugger).

`frontend/proxy.conf.json` trimite `/api` și `/hubs` către API-ul local, deci SPA-ul și API-ul sunt pe același origin și în dev — **nu avem CORS nicăieri**:

```json
{
  "/api": { "target": "http://localhost:5200", "secure": false },
  "/hubs": { "target": "http://localhost:5200", "secure": false, "ws": true }
}
```

Porturi, pe scurt: API local `5200`, API în container `8080`, Angular dev `4200`, nginx `80/443`, Seq `5341`, Postgres `5432`.

`compose.prod.yaml`, pornit cu `docker compose -f compose.yaml -f compose.prod.yaml up -d`:

```yaml
# Production: docker compose -f compose.yaml -f compose.prod.yaml up -d
# It runs tagged images. KANABOL_VERSION picks another tag than the default one.
services:
  postgres:
    restart: unless-stopped

  seq:
    restart: unless-stopped

  api:
    build: !reset null
    image: kanabol-api:${KANABOL_VERSION:-v0.1.0}
    restart: unless-stopped

  web:
    build: !reset null
    image: kanabol-web:${KANABOL_VERSION:-v0.1.0}
    restart: unless-stopped
    ports: ["80:80", "443:443"]
    volumes:
      - ./certs:/etc/nginx/certs:ro
      - ./frontend/nginx.prod.conf:/etc/nginx/conf.d/default.conf:ro
```

Expune doar `web`, pe 443 și pe 80 ca redirect către HTTPS, fără build, cu imagini tag-uite. `KANABOL_VERSION` alege alt tag decât `v0.1.0`.

Seq își păstrează retenția în volum, deci fiecare volum nou are nevoie o dată de `SEQ_ADMIN_PASSWORD=<parola> scripts/seq-retention.sh` (§8.1).

### 13.4 TLS self-signed

Fără certificat de la IT: generăm o **CA proprie** o singură dată și semnăm cu ea certificatul serverului. Colegii importă doar CA-ul (un singur fișier, o singură dată); certificatul serverului se poate reînnoi fără să-i deranjăm.

```sh
scripts/make-certs.sh kanabol.local kanabol 10.0.0.10   # numele și IP-urile reale ale serverului
```

Scriptul rulează pe server și scrie în `certs/` (în `.gitignore`). Prima rulare creează CA-ul, valabil 10 ani. Rulările următoare păstrează CA-ul și refac doar certificatul serverului, valabil 825 de zile, deci colegii nu mai importă nimic.

- `ca.crt` se distribuie colegilor (`docs/tls.md` spune cum se importă pe Windows și în Firefox). `ca.key` nu părăsește serverul.
- `nginx.prod.conf` (§13.2) are blocul `listen 443 ssl` și redirectul de pe 80.
- Cu HTTPS valid în browser, cookie-ul de refresh rămâne `Secure; SameSite=Strict` ca în spec; nu facem excepții pentru HTTP. Browserele bazate pe Chromium îl acceptă și pe `http://localhost`, deci merge și în dev.
- Dacă IT-ul oferă mai târziu un certificat, se înlocuiesc doar `server.crt`/`server.key` — nimic în cod.

`.env.example` documentează `POSTGRES_PASSWORD`, `JWT_SECRET`, `SEQ_ADMIN_HASH`, `ADMIN_INITIAL_PASSWORD` și, opțional, `KANABOL_VERSION`. `.env` e în `.gitignore`.

---

### 13.5 Livrare (grupul 19b, decis 2026-11-09)

- `scripts/backup.sh`, rulat pe server din cron: `pg_dump -Fc` prin `docker compose exec`, plus un tar al volumului de atașamente printr-un container alpine, în `backups/kanabol-AAAA-LL-ZZ/`; păstrează 14 zile; nu scrie nicio parolă în log. `scripts/restore.sh` întoarce un backup într-un stack oprit. `docs/backup.md` dă linia de cron și pașii restaurării.
- `scripts/release.sh vX.Y.Z` construiește și tag-uiește `kanabol-api` și `kanabol-web` (§13.3) și pune tag-ul git. `docs/deploy.md` dă pașii serverului: `.env`, `make-certs.sh`, `compose up`, `seq-retention.sh`, cron-ul de backup, `ca.crt` după `docs/tls.md`, actualizarea la o versiune nouă. Instalarea pe server, `ca.crt` la colegi și `compose up` sunt ale lui Florin, după `docs/deploy.md`.
- Verificarea UI (UI §15): capturi ale fiecărui ecran din UI §7, light și dark, la 1440×900, PNG în `docs/screens/` (`7.2-panou-light.png`), dintr-o bază cu date de probă; contrastul cu `axe-core` (dependență de dev) pe fiecare ecran, în ambele teme, cu rezultatele și reparațiile în `docs/ui-review.md`; `prefers-reduced-motion` verificat că oprește tranzițiile și shimmer-ul; ce pică și nu se repară pe loc intră în Blocked cu propunere.
- Detalii (decis 2026-11-09): fără registry de imagini; serverul are clona repo-ului (`compose.prod.yaml` montează `frontend/nginx.prod.conf` din ea), deci `release.sh vX.Y.Z` rulează pe server după `git pull`: verifică că e pe `main`, curat, la un commit de pe `origin/main`, construiește `kanabol-api` și `kanabol-web` cu tag-ul dat, pune tag-ul git adnotat dacă nu există, scrie `KANABOL_VERSION` în `.env` și spune comanda de `compose up`; `v0.1.0` îl face și îl împinge implementatorul după ce grupul 19b e în `main`. `backup.sh` rulează din rădăcina clonei cu `compose.yaml` și `compose.prod.yaml`: `pg_dump -Fc` ca utilizatorul `postgres` din container (fără parolă pe linia de comandă) în `backups/kanabol-AAAA-LL-ZZ/db.dump`, volumul `attachments` în `attachments.tar.gz` printr-un container alpine, șterge folderele mai vechi de 14 zile, scrie un rând în `backups/backup.log`; `restore.sh <folder>` cere confirmare (sau `--yes`), oprește `api` și `web`, `pg_restore --clean --if-exists` peste bază, golește și rescrie volumul, pornește stack-ul; cron-ul zilnic la 02:00. Datele de probă pentru capturi și demonstrații vin din `scripts/demo-data.sh`, comis, prin API: 3 utilizatori, 2 proiecte cu coloane, vreo 15 task-uri cu deadline-uri, comentarii, un fișier, notificări; rulat de două ori nu face dubluri. `axe-core` îl rulează implementatorul în Browser pane pe fiecare ecran, în ambele teme (un Playwright ar aduce un browser în repo), iar `check-release.sh` 3 verifică doar că există documentele și capturile fiecărui ecran, light și dark.

---

## 14. Pipeline local la commit (fără runner)

Cerință: testele rulează **mereu înainte de un commit pe `main`**, pe mașina dezvoltatorului, fără CI extern. Pe celelalte branch-uri, inclusiv `dev`, rulăm doar verificări rapide ca să nu încetinim munca.

Branch-uri: fiecare task se lucrează pe un branch `feature/<kebab>` (sau `fix/`, `chore/`, `docs/`, §7.5) pornit din `dev`. Când un grup din `Tasks.md` e gata, branch-ul intră în `dev`; aici fast-forward e permis, pentru că fiecare commit a trecut deja prin `pre-commit`. `dev` intră în `main` **numai după ce verificările de acceptanță ale grupului (§14.7) au trecut pe o clonă curată**, mereu cu `--no-ff`, ca merge commit-ul să ruleze suita completă. `main` conține deci doar grupuri terminate și verificate, iar `dev` e locul unde se adună munca dintre două merge-uri în `main`.

### 14.1 Tooling

`package.json` la rădăcina repo (nu e frontend-ul; e doar pentru hook-uri):

```json
{
  "name": "kanabol-repo",
  "private": true,
  "scripts": {
    "prepare": "husky"
  },
  "devDependencies": {
    "@commitlint/cli": "^19",
    "@commitlint/config-conventional": "^19",
    "husky": "^9",
    "lint-staged": "^15",
    "prettier": "^3.8.1"
  }
}
```

`npm install` la rădăcină (rulat de `scripts/setup.sh`) instalează hook-urile din `.husky/`. Hook-urile sunt în repo, deci orice clonă le primește. `prettier` e aici pentru Markdown și YAML din rădăcină, iar `.prettierignore` îl ține departe de `docs/SPEC.md` și de copiile din vault (`docs/UI_UX_SPEC.md`, `docs/adr/`).

### 14.2 Hook-uri

```sh
# .husky/pre-commit
#!/bin/sh
exec scripts/verify.sh commit
```

```sh
# .husky/pre-merge-commit
#!/bin/sh
# git merge --no-ff creates a commit, but doesn't call pre-commit.
exec scripts/verify.sh merge
```

```sh
# .husky/commit-msg
#!/bin/sh
npx --no -- commitlint --edit "$1"
```

```sh
# .husky/pre-push
#!/bin/sh
# The safety net for a commit made with --no-verify.
while read -r local_ref local_sha remote_ref remote_sha; do
  case "$remote_ref" in
    refs/heads/main)
      scripts/require-docker.sh || exit 1
      exec scripts/test-all.sh
      ;;
  esac
done
```

### 14.3 `scripts/verify.sh`

```sh
#!/bin/sh
# What the commit and merge hooks run (docs/SPEC.md section 14.3): the full suite on main, fast checks elsewhere.
# Usage: scripts/verify.sh [commit|merge]

set -e
cd "$(git rev-parse --show-toplevel)"
branch=$(git rev-parse --abbrev-ref HEAD)
mode=${1:-commit}

if [ "$branch" = "main" ]; then
  scripts/require-docker.sh
fi

echo "▶ $mode: lint-staged"
# eslint, prettier and dotnet format on the staged files only. dotnet format ignores absolute paths
# without saying so, and lint-staged sends those unless told otherwise.
npx --no -- lint-staged --relative

if [ "$branch" = "main" ]; then
  echo "▶ main: full test suite"
  exec scripts/test-all.sh
fi

echo "▶ $branch: fast checks"
if git diff --cached --name-only | grep -q '^backend/'; then
  dotnet build backend/Kanabol.sln -warnaserror --nologo -v q
  dotnet test backend/tests/Kanabol.Domain.Tests --no-build --nologo -v q
fi
if git diff --cached --name-only | grep -q '^frontend/'; then
  (cd frontend && npx ng test --no-watch)
fi
```

`scripts/require-docker.sh` oprește cu un mesaj clar când Docker e oprit. Altfel suita completă ar raporta doar o listă de teste picate, fără să pomenească de Docker.

### 14.4 `scripts/test-all.sh`

```sh
#!/bin/sh
set -e
cd "$(git rev-parse --show-toplevel)"
echo "▶ backend: build + all tests (Testcontainers needs Docker running)"
dotnet build backend/Kanabol.sln -warnaserror --nologo -v q
dotnet test backend/Kanabol.sln --no-build --nologo -v q
echo "▶ frontend: lint + tests"
cd frontend
npx ng lint
npx ng test --no-watch
echo "✔ all green"
```

### 14.5 `.lintstagedrc.json`

În rădăcină:

```json
{
  "backend/**/*.cs": ["dotnet format backend/Kanabol.sln --include"],
  "*.{md,yaml,yml}": ["prettier --write"]
}
```

`frontend/.lintstagedrc.json`, separat, ca eslint și prettier să ruleze din `frontend/`, cu configurațiile de acolo:

```json
{
  "*.{ts,html}": ["prettier --write", "eslint --fix --max-warnings=0"],
  "*.{scss,json,md,yaml,yml}": ["prettier --write"]
}
```

### 14.6 Reguli de git care fac hook-urile de încredere

`scripts/setup.sh`, rulat după clone (se poate rula din nou):

```sh
#!/bin/sh
# Prepares a clone: the packages, the hooks and the git settings they rely on (docs/SPEC.md section 14.6).
# Usage: scripts/setup.sh    (safe to run again)

set -e
cd "$(git rev-parse --show-toplevel)"

# The root package's prepare script makes git use the hooks in .husky/.
npm install --no-audit --no-fund
(cd frontend && npm install --no-audit --no-fund)
dotnet tool restore

# A fast-forward runs no hook, so a merge into main always has to create a commit.
git config branch.main.mergeOptions "--no-ff"
git config commit.gpgsign false

echo "Ready. The hooks are on: docs/SPEC.md section 14 says what they run."
```

Observații:
- **Fast-forward nu rulează niciun hook** — de aceea forțăm `--no-ff` pe `main`. Merge commit-ul trece prin `pre-merge-commit` → `verify.sh` → suita completă. Pe `dev` nu forțăm nimic: un merge fast-forward acolo aduce doar commit-uri care au trecut deja prin `pre-commit`.
- `core.hooksPath` rămâne `.husky/_`, cum îl setează husky 9; nu-l suprascriem.
- Testele de integrare au nevoie de **Docker Desktop pornit** (Testcontainers). Dacă nu e pornit, `scripts/require-docker.sh` refuză commit-ul pe `main` cu un mesaj clar — corect, nu sărim testele.
- `git commit --no-verify` rămâne posibil (git nu poate interzice); `pre-push` pe `main` e plasa de siguranță.
- Durată estimată pe `main`: build ~15 s + container Postgres ~8 s + teste; limita e 180 s total (ridicată de la 90 s pe 2026-11-06: suita dura 56–73 s și varia cu până la 14 s între rulări identice, iar fiecare grup nou mai adaugă câteva secunde; și de la 150 s pe 2026-11-07: după grupul 14, cu teste care deschid WebSocket-uri reale, merge-ul ținea 128 s pe o mașină liniștită și 155–157 s pe una încărcată). Pe feature branches limita e 40 s: un commit care atinge și backend, și frontend durează 21–22 s, unul pe o singură parte 14–18 s. Măsurat la gate-uri: 164 s la grupul 17a; 188 s la grupul 18 pe 21a92a0, pentru că fiecare test al rapoartelor hash-uia parola la fiecare utilizator făcut; 172 s după 4f69e63, unde testele rapoartelor nu mai hash-uiesc. Hash-ul parolei comune de test calculat o dată pe suită, în `AuthApiTest` și `AuthHandlerTest`, e un task al grupului 19; ținta rămâne 180 s (decis 2026-11-09). După grupul 19, cu hash-ul o dată pe suită: Application 1 min 59 s → 1 min 3 s, Api 2 min 14 s → 1 min 34 s pe mașina de lucru; merge-ul în `main` 145 s pe clona proaspătă (`check-pipeline.sh 6`) și 165 s la merge-ul real (929be11).
- Pe Windows, hook-urile rulează prin `sh` din Git for Windows; scripturile sunt POSIX sh (fără bash-isme) și au `eol=lf` prin `.gitattributes`.

### 14.7 Verificări de acceptanță

Fiecare grup din `Tasks.md` are verificările lui în `scripts/check-*.sh`. Nu rulează în hook-uri: se rulează toate pe o clonă curată, după `scripts/setup.sh`, înainte ca un grup să fie considerat gata, pentru că repo-ul n-are CI.

- `check-tests.sh`, `check-auth.sh`, `check-permissions.sh`, `check-users.sh`, `check-admin.sh`, `check-projects.sh`, `check-tasks.sh`, `check-board.sh`, `check-assignees.sh`, `check-realtime.sh`, `check-comments.sh`, `check-history.sh`, `check-alerts.sh`, `check-dashboard.sh`, `check-reports.sh`, `check-hardening.sh`, `check-release.sh`, `check-pipeline.sh` și `check-docker.sh` au nevoie de Docker pornit.
- `check-backend.sh`, `check-auth.sh`, `check-permissions.sh`, `check-users.sh`, `check-admin.sh`, `check-projects.sh`, `check-tasks.sh`, `check-board.sh`, `check-assignees.sh`, `check-realtime.sh`, `check-comments.sh`, `check-history.sh`, `check-alerts.sh`, `check-dashboard.sh`, `check-reports.sh` și `check-hardening.sh` au nevoie de un PostgreSQL al cărui utilizator poate crea baze de date, din `KANABOL_CHECK_DB` sau din user secret-ul `ConnectionStrings:Default`. Lucrează în baze proprii și le șterg la final.
- `check-docker.sh 5` are nevoie de porturile 5432, 5341 și 5200 libere.
- `check-web.sh` (grupul 9) n-are nevoie de Docker sau de PostgreSQL, doar de pachetele instalate de `scripts/setup.sh`.
- `check-admin.sh` (grupul 10): task-urile 1 și 2 au nevoie doar de pachete; task-ul 3 rulează testele endpoint-ului și pornește API-ul real în Production, deci are nevoie de Docker și de PostgreSQL, ca `check-users.sh`.
- `check-projects.sh` (grupul 11): task-urile 1, 2 și 4 rulează testele și pornesc API-ul real în Production, deci au nevoie de Docker și de PostgreSQL; task-ul 3 are nevoie doar de pachete.
- `check-tasks.sh` (grupul 12; task-ul 12.1 e în `check-projects.sh` 11.4): task-urile 3, 4, 5, 6 și 9 rulează testele și pornesc API-ul real în Production, deci au nevoie de Docker și de PostgreSQL; 2 rulează doar testele de domeniu și build-ul; 7 și 8 au nevoie doar de pachete.
- `check-board.sh` (grupul 13): task-urile 1, 2 și 3 rulează testele și pornesc API-ul real în Production, deci au nevoie de Docker și de PostgreSQL (1 rulează și `check-tasks.sh` 3, cu cele 10 creări simultane); 4, 5 și 6 au nevoie doar de pachete.
- `check-assignees.sh` (grupul 13b): task-urile 1, 2, 3 și 6 au nevoie de Docker și de PostgreSQL (1 migrează o bază veche proprie și înapoi, 2 și 3 pornesc API-ul real în Production, 6 rulează și `check-backend.sh` 8 9, schema și tabelele după migrare); 4 și 5 au nevoie doar de pachete.
- `check-realtime.sh` (grupul 14): task-urile 1 (hub-ul, token-ul din query string și `X-Connection-Id`: testele, apoi API-ul real în Production și în Development printr-un WebSocket, și logurile fără token) și 2 (evenimentele domeniului pe hub: testele, apoi fiecare schimbare pe API-ul real, ascultată de două board-uri și de un outsider) au nevoie de Docker și de PostgreSQL; 3 (`Realtime`, interceptorul și board-ul: specuri, lint, formatare, build-ul de producție) doar de pachete; 4 (fluxurile grupului cap-coadă în testele API-ului și spec-ul board-ului pentru propriul tab) de Docker și de pachete. Scriptul își scrie un client SignalR mic în Node, care vorbește cu hub-ul ca un browser, cu token-ul doar în adresă. `check-docker.sh` 2 verifică și că token-ul din adresa unui hub lipsește din access log-ul nginx, cu contraprobă.
- `check-comments.sh` (grupul 15): task-urile 1 (comentariile, notificarea și `CommentAdded`: testele, apoi API-ul real în Production), 2 (fișierele, `LocalFileStorage` și `TaskUpdated` cu `Attachments`: testele, apoi API-ul real în Production, cu ce ține pe disc și cu lista suprascrisă prin `Storage__AllowedExtensions`) și 4 (fluxurile grupului în testele API-ului, apoi un comentariu și un fișier pe API-ul real, auzite de trei board-uri printr-un WebSocket real și de niciun outsider) au nevoie de Docker și de PostgreSQL; 3 (cele două tab-uri ale panoului și board-ul care ascultă: specuri, lint, formatare, build-ul de producție, token-urile tab-urilor în CSS) doar de pachete. Scriptul își scrie clientul SignalR al lui `check-realtime.sh`, cu un pas în plus pentru upload (decis 2026-11-08).
- `check-history.sh` (grupul 16): task-urile 1 (`GET /api/tasks/{id}/history`: testele, apoi API-ul real în Production: pagina de la cea mai nouă, 50 implicit, cine a schimbat, numele asignaților și ale coloanelor, un task arhivat, un outsider), 4 (`Storage:AllowedExtensions` ca o singură valoare: testele, apoi API-ul real pe lista implicită, pe `Storage__AllowedExtensions=pdf,png` din comentariul din compose, care înlocuiește toată lista, și oprirea la pornire cu o listă pe indici) și 5 (fluxurile grupului în testele API-ului, apoi fiecare schimbare pe API-ul real) au nevoie de Docker și de PostgreSQL; 2 (tab-ul „Istoric”: specuri, lint, formatare, build-ul de producție) și 3 (select-ul de prioritate cu chip-ul în trigger: la fel) doar de pachete (decis 2026-11-08). Task-ul 6 (grupul 16b, decis 2026-11-08): textele descrierii la cerere și diferența lor: testele handler-ului, ale endpoint-ului și fluxurile, apoi API-ul real în Production (lista fără texte, cu ele în baza de date; intrarea cu ambele texte cum s-au salvat, cu rândurile și diacriticele; 404 `History.NotFound` pe intrarea altui task și pe una care nu există; un outsider; un task arhivat), apoi blocul diferenței: specuri, lint, formatare, build-ul de producție, pachetul `diff` 9 doar în chunk-ul panoului task-ului, în niciun fișier pe care îl încarcă `index.html`; are nevoie de Docker, de PostgreSQL și de pachete.
- `check-alerts.sh` (grupul 17): task-urile 1 (worker-ul și `NotifyOverdueTasksCommand`: testele, apoi API-ul real în Production cu intervalul cel mai scurt, un minut: rularea de la pornire, o alertă pe persoană, la asignați și owneri, nimic altcuiva, nimic pentru un task terminat, arhivat, nescadent sau dintr-un proiect arhivat, un asignat adăugat mai târziu alertat la rularea următoare și nimeni de două ori, oprirea la pornire sub un minut, verificarea oprită), 2 (endpoint-urile notificărilor: testele, apoi API-ul real în Production pe notificări făcute de un comentariu, două asignări și verificare, cu oameni proprii: ale celui care cere, `Page` de 20 de la cea mai nouă, filtrele, 400 pe o interogare greșită, un outsider, numărul necitit, `read` de două ori, 404 pe a altcuiva, `read-all`) și 4 (fluxurile grupului în `OverdueAlertsFlowTests`, apoi API-ul real cu verificarea la un minut: alerta auzită de oamenii ei printr-un WebSocket real, conectați ca clopoțelul, fără board, și de niciun membru sau outsider; scriptul își scrie clientul SignalR în Node) au nevoie de Docker și de PostgreSQL; 3 (clopoțelul, meniul și pagina: specuri, lint, formatare, build-ul de producție fără alte avertismente decât bugetul, textele din UI §7.6, `Realtime` doar prin `import()`, SignalR în afara bundle-ului inițial, într-un chunk comun cu board-ul) doar de pachete (decis 2026-11-08).
- `check-dashboard.sh` (grupul 17a): task-urile 1 (`GET /api/me/summary` și `GET /api/me/tasks`: testele, apoi API-ul real în Production pe proiectele PAN, MEU și ARH, arhivat, cu un task arhivat, unul terminat, unul întârziat, unul al adminului și unul arhivat după ce s-a terminat: cele patru numere, și pentru un `weekStart` cu offset, 400 pe `weekStart` lipsă, greșit sau în viitor, ale lui Dan după deadline cu proiectul pe rând, `open=true`, filtrele, `includeArchived` fără `tasks.archive` refuzat, nimic pentru `projects.view_all` sau un outsider) și 2 (`GET /api/projects` cu `mine=true` și `completedTaskCount`: testele, apoi API-ul real pe aceleași proiecte: doar cele în care e membru, și pentru `projects.view_all`, arhivatele doar cu `includeArchived`, task-ul terminat și apoi arhivat nenumărat, 400 pe `mine` care nu e boolean) au nevoie de Docker și de PostgreSQL; 3 (panoul și „Task-urile mele”: specuri, lint, formatare, build-ul de producție fără alte avertismente decât bugetul, textele din UI §7.2, panoul fără importuri din alte feature-uri, tabelul și bara de filtre ale listei pe `/my-tasks`, nicio bucată a celor două pagini în fișierele pe care le încarcă `index.html`; „bundle-ul inițial neschimbat” nu ține la byte când se schimbă o rută: cele două rute noi au adăugat 0,60 kB brut, iar scriptul verifică lipsa paginilor din bundle, cu contraprobă) doar de pachete; 4 (fluxurile grupului în `DashboardFlowTests`, apoi aceleași schimbări pe API-ul real: un task nou al lui Dan după deadline, terminat, arhivat, restaurat, redeschis, cu deadline mutat, Dan scos de pe el, Dan scos din MEU, PAN arhivat și restaurat, fiecare schimbând cele patru numere, task-urile și cardurile lui cum spun regulile) de Docker și de PostgreSQL. API pe portul 5293, baza `kanabol_dashboard_check_$$` (decis 2026-11-09).
- `check-reports.sh` (grupul 18): task-urile 1 (cele trei query-uri: testele handler-elor și ale endpoint-urilor, apoi API-ul real în Production pe munca făcută prin API, cu momentele mutate în baza de date în octombrie 2026: PAN, MEU și ARH, arhivat la final; un task cu doi asignați, unul arhivat după finalizare, unul creat direct în „În lucru”, unul fără deadline, unul al nimănui, unul deschis întârziat și unul întârziat într-un proiect arhivat; totalul, pe om, pe proiect, perioadele pe săptămână și pe lună, octombrie de la +03:00 la +02:00 ca o singură perioadă, proiect și om; lead și cycle time cu media și mediana, timpul pe coloană doar cu proiect; întârziatele de azi indiferent de interval, la timp, după termen, rata și perioadele; 400 pe `from`, `to` și `granularity`, 404 pentru un proiect inexistent) și 2 (cine le citește: pentru fiecare raport, `reports.view` fără proiect, un owner fără permisiune pe proiectul lui, și arhivat, și pentru orice om, 400 pe `projectId` lipsă, 403 pe al altuia, `projects.manage`, un membru simplu, `projects.view_all` și un outsider cu 403 `Project.OwnerRequired`, 404 pentru un proiect inexistent oricui, numele permisiunii doar în warning-ul din log, un owner dezactivat între timp refuzat cu token-ul pe care îl mai are) au nevoie de Docker și de PostgreSQL; 3 (pagina, tab-ul proiectului și `kb-chart`: specuri, lint, formatare, build-ul de producție fără alte avertismente decât bugetul, textele din UI §7.8, filtrele în adresă, tab-ul care încarcă pagina cu proiectul fixat și fără placeholder, duratele, CSV-ul, `chart.js` 4 cu doar piesele barelor și culorile din token-uri, nicio importare din alt feature, `chart.js` într-un chunk al lui, adus de `@defer`, și nici el, nici pagina în fișierele încărcate de `index.html`, pagina în chunk-ul ei și nu în al proiectelor; scrie „Initial total”: 893,03 kB, +6,63 kB față de `dev` din runtime-ul `@defer`, acceptat) doar de pachete; 4 (fluxurile grupului în `ReportsFlowTests`, apoi aceleași schimbări pe API-ul real, într-un interval în jurul lui acum: un task al lui Dan finalizat la timp, mutat înapoi, finalizat iar, arhivat și restaurat, cu deadline-ul mutat înaintea finalizării; Dan, făcut owner, citește aceleași numere fără `reports.view`) de Docker și de PostgreSQL. API pe portul 5285, baza `kanabol_reports_check_$$`. `check-projects.sh` 3 nu mai cere `project-placeholder.ts`, care a dispărut (decis 2026-11-09).
- `check-hardening.sh` (grupul 19): task-urile 1 (headerele: cele cinci din §13.2, CSP-ul inclus, o dată fiecare, la nivel de server, pe imaginea construită din `frontend/`: aplicația, un deep link, un script, manifestul, ce răspunde API-ul și 413-ul lui nginx; HSTS doar în `nginx.prod.conf`, pe 443, un an fără preload, nu și pe redirect-ul de pe 80; `index.html` construit fără nimic ce ar bloca CSP-ul: niciun script sau handler inline, niciun stylesheet pe `media="print"`, nimic din alt origin; contraprobe: un header într-un `location`, unul fără `always`, scriptul inline al build-ului) are nevoie de Docker; 2 (limitele hub-ului: testele, apoi API-ul real în Production printr-un WebSocket real, cu un client SignalR în Node: 8 proiecte pe o conexiune și al 9-lea refuzat cu `Hub.TooManyProjects`, locul eliberat de `LeaveProject`, 4 refuzuri păstrează conexiunea și al 5-lea o închide, cu warning în log, un membru scos nu mai aude nimic din proiect, iar altul da, schimbarea rolului și dezactivarea închid toate conexiunile omului și ale nimănui altcuiva, iar token-ul pe care îl mai are nu mai intră în niciun proiect), 3 (trimiterile: testele, apoi API-ul real în Production: un board aude un task creat, mutat, redenumit, arhivat și restaurat în ordinea asta; un board care a intrat într-un proiect și nu mai citește din socket, cât 20 de workeri mută task-uri 40 de secunde: nicio cerere nu ia 3 secunde, iar serverul închide board-ul blocat, deci scrierile spre el s-au blocat de-adevăratelea (notifier-ul vechi ținea una 9,3 s); apoi redactarea: testele pe un token într-o listă, un obiect, un dicționar, și în chei, un `Uri`, mai adânc, valorile fără token păstrate, prin pipeline-ul Serilog al API-ului, apoi API-ul real în Development, care scrie negocierea și WebSocket-ul unui board cu adresa întreagă: `access_token=[hidden]`, niciun token) și 4 (curățarea refresh token-urilor: testele, apoi API-ul real în Production pe rânduri făcute direct în bază: oprită le păstrează pe toate și o spune; la pornire le șterge pe cele expirate sau revocate de peste 30 de zile, 3 din 6, cu numărul în log, iar sesiunile vii ale adminului rămân; `Auth__RefreshTokenCleanupInterval=00:30:00` oprește pornirea) au nevoie de Docker și de PostgreSQL; 5 (bundle-ul și culorile graficelor: specuri, lint, formatare, build-ul de producție fără niciun avertisment, bugetul la 750 kB și eroarea la 1 MB, „Initial total” sub el și sub 800 kB, tipărit; nimic din paginator, `mat-select`, slide toggle, `@angular/forms`, `chart.js` sau SignalR în fișierele pe care le încarcă `index.html`, fiecare într-un chunk leneș; fiecare componentă cu `mat-paginator` își dă etichetele; `--kb-chart-1` și `--kb-chart-2` în CSS-ul construit, în light și în ambele reguli dark, citite de `kb-chart`) doar de pachete; 6 (hash-ul o dată pe suită: testele, niciun helper care hash-uiește pe om, apoi `check-pipeline.sh 6`, cu merge-ul în `main` sub 180 s, timpul tipărit) de Docker. API pe portul 5284, baza `kanabol_hardening_check_$$` (decis 2026-11-09).
- `check-release.sh` (grupul 19b, de creat de grup): task-urile 1 (`backup.sh` și `restore.sh` pe un stack de probă, cu un backup întors într-o bază goală și în volumul de atașamente: Docker), 2 (`release.sh` construiește imaginile tag-uite pe o clonă curată, `compose.prod.yaml` le pornește cu `KANABOL_VERSION`-ul scris în `.env`: Docker) și 4 (`demo-data.sh` pe un API real, pornit de două ori fără dubluri: Docker și PostgreSQL) au nevoie de Docker; 3 (`docs/deploy.md`, `docs/backup.md`, `docs/ui-review.md` și capturile fiecărui ecran din UI §7, light și dark, în `docs/screens/`) doar de pachete. Grupul scrie aici ce face fiecare task (decis 2026-11-09).

Dacă mai târziu apare un server de CI (GitHub Actions, Gitea), `scripts/test-all.sh` e exact ce rulează și acolo — fără duplicare de logică.

---

## 15. Configurare și secrete

- `appsettings.json` (valori comune) + `appsettings.Development.json` (loguri verbose, Swagger). Fără CORS: în dev `proxy.conf.json`, în prod nginx — același origin peste tot.
- Secrete în dev: `dotnet user-secrets` (`Jwt:Secret`, `ConnectionStrings:Default`, `Admin:InitialPassword`). Override-uri locale **nesecrete** pot sta în `appsettings.Development.local.json` (deja în `.gitignore`), încărcat explicit în `Program.cs` după cel standard. În Docker/prod: variabile de mediu (`__` ca separator).
- Toate opțiunile tipate (`JwtOptions` etc.) cu `[Required]` și `ValidateOnStart()` → aplicația refuză să pornească cu config incompletă, nu eșuează la primul request.
- Frontend: `environment.ts` conține doar `apiBaseUrl` (`/api`) și `hubsBaseUrl` (`/hubs`); același build pentru toate mediile, nginx face restul.

---

## 16. Securitate de bază (checklist v1)

- [ ] HTTPS terminat în nginx cu CA proprie + certificat self-signed (§13.4); `ca.crt` importat pe stațiile colegilor; HTTP doar redirect.
- [ ] `HSTS`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` și CSP-ul din §13.2 (grupul 19; decis 2026-11-09).
- [ ] Rate limiting pe login; lockout 15 min după 10 eșecuri consecutive (câmp `locked_until` pe `users`).
- [ ] Parole: minim 10 caractere, verificate contra unei liste scurte de parole comune.
- [ ] Refresh token rotit, revocare la logout, la schimbarea parolei, la dezactivare și la resetarea parolei de către admin. Parola proprie se schimbă doar cu cea curentă, niciodată prin endpoint-ul de resetare (§4.4).
- [ ] Validare de input în toate commands (FluentValidation); EF parametrizat → fără SQL injection.
- [ ] Atașamente: extensii permise, dimensiune max, nume generat, `Content-Disposition: attachment`, nu se execută niciodată din `wwwroot`.
- [ ] Backend verifică permisiunea și accesul la proiect la fiecare request; UI doar ascunde.
- [ ] Markdown-ul din descrieri și comentarii se randează în client prin parserul propriu (`shared/ui/markdown-parser.ts`, `kb-markdown`), care citește textul într-un arbore randat prin template-uri, deci nimic nu ajunge în pagină ca HTML: HTML-ul brut e eliminat, linkurile merg doar la http, https și mailto, cu `rel="noopener noreferrer"` și `target="_blank"`; niciodată `innerHTML` sau `bypassSecurityTrust*` (`check-tasks.sh` 7 le refuză). Sintaxa: paragrafe cu rânduri noi, titluri `#`–`###`, bold, italic, cod inline și blocuri, liste, citate, linii, linkuri și adrese scrise ca atare. Fără `marked` sau `DOMPurify` (decis 2026-11-06).
- [ ] Health endpoint `/health` fără date sensibile; Swagger doar în Development.
- [ ] Backup zilnic `pg_dump` (cron pe server) + volumul de atașamente.

---

## 17. Decizii (ADR) de adăugat în vault

Pe lângă cele două existente (`RBAC pe două niveluri`, `Un singur repo`), decizia de acum produce câte o notă în `Decisions/`:

1. Clean Architecture + CQRS light fără MediatR
2. Utilizatori proprii + JWT cu refresh rotit (nu Identity, nu AD)
3. Angular Signals + servicii, fără NgRx; Angular Material
4. Serilog + Seq pentru loguri; istoricul de task-uri e în DB, nu în loguri
5. Postgres real în teste (Testcontainers), nu InMemory
6. Husky + lint-staged; suita completă la commit/merge pe `main`, `--no-ff` obligatoriu
7. SignalR din v1, alimentat de domain events
8. Atașamente pe disc în spatele `IFileStorage`
9. Fără MediatR / AutoMapper / Moq / Karma (motivele din §2)
10. Un singur rol per utilizator (`users.role_id`), nu many-to-many
11. Arhivare în loc de ștergere pentru task-uri și proiecte
12. Alerte doar in-app în v1, fără canal de email pregătit
13. TLS cu CA proprie + certificat self-signed, nu certificat de la IT
14. Productivitate = finalizate / lead & cycle time / deadline; fără story points

Format recomandat pentru fiecare: `Decizie / Motiv / Alternative / Legate`, ca cele existente.

---

## 18. Întrebări deschise

Toate întrebările din v1.0 au fost închise pe 2026-11-04 (vezi §0 și §17). Rămân pentru momentul implementării, fără impact pe arhitectură:

- Hostname-ul/IP-ul intern al serverului (le primește `scripts/make-certs.sh`, §13.4).
- Intervalul `OverdueTasksWorker`: 15 min implicit, configurabil prin `Alerts:OverdueCheckInterval` (§4.3; decis 2026-11-08).
- Lista de extensii permise la atașamente.

---

## 19. Ordinea de lucru (mapare pe `Tasks.md` → Now)

1. **Schelet repo**: structura din §3, `.editorconfig`, `Directory.Build.props`, `package.json` root, `scripts/setup.sh`.
2. **Lint**: ESLint/Prettier în frontend, analizoare în backend; verifică `dotnet format` și `ng lint` curate pe proiectele goale.
3. **Backend + DB**: cele 4 proiecte, `AppDbContext`, `Entity`/`AuditableEntity`, entitățile din §5.1, prima migrare, Serilog + correlation id, health endpoint, Swagger.
4. **Teste**: fixture Testcontainers, un test de domeniu și un test de handler ca șablon; Vitest cu un spec de store.
5. **Docker**: Dockerfiles + compose; `docker compose up postgres seq` funcțional, Seq primește loguri.
6. **Hook-uri**: Husky, `verify.sh`, `test-all.sh`, `--no-ff`; primul commit pe `main` trece prin suita completă.

Apoi `Next` din `Tasks.md`, în ordinea existentă (auth → RBAC → proiecte → task-uri → kanban → …).
