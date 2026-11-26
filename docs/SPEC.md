# SPEC — Kanabol (Kanban pe steroizi)

> Specificație tehnică pentru prima versiune. Produsul și regulile de business sunt în vault-ul Obsidian (`_Overview.md`, `_Architecture.md`, `Decisions/`). Acest fișier stabilește **cum** construim: arhitectură, pattern-uri, convenții, loguri, teste, lint, Docker și pipeline-ul local de commit.
>
> Stare: v2.2 — 2026-11-26, cu regulile pasului 2 al chat-ului cu RAG în §20, scrise înaintea codului; v2.1 (2026-11-22) a adus pasul 1, în §19. De la v2.0 (2026-11-13) spec-ul ține doar regulile: arhitectură, pattern-uri, convenții, loguri, teste, lint, Docker și pipeline. Cum e implementat fiecare lucru se vede în cod, în teste și în capul scripturilor `scripts/check-*.sh`. Istoricul e în git (`git log -- docs/SPEC.md`); v1.34 e ultima versiune cu detaliile de implementare ale fiecărui grup.
>
> Cum se ține la zi: un gol mic îl hotărăște cine implementează și îl scrie în mesajul commit-ului, fără să-l adauge aici. O regulă de aici se schimbă în același commit cu codul care o schimbă. La Florin ajung doar deciziile de produs, adică ce vede sau ce poate face un utilizator.

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
| Git | Feature branches → `dev` → `main`, Conventional Commits, **Husky + lint-staged** cu verificări rapide pe orice branch; suita completă rulează în workflow-ul GitHub, iar gate-ul întreg rulează acolo doar la push pe `main`, când intră `dev` (§14) |
| Deploy | Server Linux, Docker Compose, nginx servește SPA + proxy către API |
| TLS | Certificat **self-signed** (CA proprie), terminat în nginx, importat o dată în browserele colegilor |
| Căutare după înțeles | Vectori în PostgreSQL-ul aplicației, prin **`pgvector`**; modelele AI doar în spatele unor interfețe, oprite implicit (§19) |
| Chat | Un model de chat scrie răspunsul doar din ce întorc uneltele; uneltele le execută aplicația, care stabilește și sursele; modelul nu atinge baza; oprit implicit (§20) |

---

## 1. Scop și limite

Task manager intern, tip Jira-light: utilizatori + RBAC, proiecte, task-uri cu prioritate, kanban configurabil cu drag & drop, comentarii + atașamente, istoric modificări, alerte deadline, productivitate per dezvoltator/proiect. Fără integrări externe, fără mobil, fără LLM în v1. După v1.2, un chat care răspunde din datele aplicației se construiește pe pași, cu modelele AI ca singura integrare externă; regulile lui sunt în §19 și §20.

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
| PostgreSQL | 17.11, pe Alpine 3.24, cu `pgvector` 0.8.7 | stabil, suportat de Npgsql 10; imagine proprie, `database/Dockerfile` (§19.1) |
| Node.js | 22 LTS | cerut de Angular 22 |
| Angular | 22.x | curent (iunie 2026); upgrade la 24 LTS când apare |
| Angular Material + CDK | 22.x | aceeași versiune cu Angular |
| Seq | ultimul `datalust/seq` | gratuit pentru un singur utilizator |

Pachete NuGet cheie: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pgvector.EntityFrameworkCore` 0.3.0 (tipurile și distanțele `pgvector` în EF, prin `UseVector()`, §19.1), `Azure.AI.OpenAI` 2.1.0 (ultima stabilă; doar în Infrastructure, pentru `AzureEmbeddingModel`, §19.3), `Microsoft.ML.Tokenizers` 3.0.0 și `Microsoft.ML.Tokenizers.Data.Cl100kBase` (doar în Infrastructure: numără tokenii ca modelele OpenAI, ca un text lung să fie scurtat la limita lor, §19.5), `EFCore.NamingConventions`, `FluentValidation`, `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Swashbuckle.AspNetCore`, `Scrutor` (decoratori), `Testcontainers.PostgreSql`, `Respawn`, `xunit`, `NSubstitute`, `FluentAssertions`, `Microsoft.AspNetCore.SignalR.Client` (doar în `Kanabol.Api.Tests`, ca testele să se conecteze la hub ca un browser; decis 2026-11-07).

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
├── database/
│   └── Dockerfile                # PostgreSQL cu pgvector, construit de compose și de teste (§19.1)
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
- Un handler = o tranzacție = un `SaveChangesAsync`. `DbContext` este Unit of Work; **nu** facem Repository generic peste EF (abstracție inutilă). `IAppDbContext` expune doar `DbSet`-urile și `SaveChangesAsync`. Excepția: crearea unui task își ia numărul prin `ITaskNumbers.SaveNewTaskAsync`, care deschide singură tranzacția (§5.1).
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
| **Background service** | `OverdueTasksWorker : BackgroundService` cu `PeriodicTimer`, în `Infrastructure`, pornit după migrații: la pornire și apoi la fiecare `Alerts:OverdueCheckInterval` rulează `NotifyOverdueTasksCommand` din `Application`, într-un scope propriu. O alertă pe task și pe om, o singură dată; o rulare care pică e logată și nu oprește worker-ul | Alerte deadline in-app, fără scheduler extern |
| **Domain event handlers** pentru notificări | `TaskAssignedEvent` → `Notification(TaskAssigned)` pentru cei nou adăugați, fără cel care face schimbarea; `CommentAddedEvent` → `Notification(CommentAdded)` pentru asignații și creatorul task-ului care sunt membri activi, fără autor; ambele → `NotificationCreatedEvent` → SignalR `user:{id}`. `Message` e în engleză, ca API-ul | Clopoțelul e util din prima zi, nu doar la întârzieri |

### 4.4 Autorizare

**Nivel aplicație — permisiuni.** Codul verifică permisiuni, niciodată nume de roluri. Lista inițială (constante în `Kanabol.Domain.Authorization.Permissions`):

```
users.manage        roles.manage        projects.create
projects.view_all   projects.manage     tasks.create
tasks.archive       reports.view        settings.manage
columns.manage      chat.use
```

`chat.use` (grupul 25) e a căutării după sens și, din pasul 2, a chat-ului. Citește singură peste toate proiectele, fără `projects.view_all`, deci se dă doar cui are voie să vadă tot (§19.6).

`tasks.create` se verifică pe lângă apartenența la proiect: creezi task-uri doar în proiectele în care ești membru și doar dacă rolul tău are permisiunea (UI §7.4). Apartenența e cea reală, un rând în `project_members`, nu cea derivată din `projects.view_all` sau `projects.manage`: cine administrează un proiect străin se adaugă întâi ca membru. Un rol poate deci vedea un proiect fără să poată adăuga task-uri în el.

**Schimbarea unui task.** Orice schimbare a unui task (titlu, descriere, asignați, prioritate, deadline, mutare, arhivare) cere aceeași apartenență reală ca la creare: rând în `project_members` și cont activ (`IProjectAccess.EnsureRealMemberAsync`). `projects.view_all` și `projects.manage` citesc, dar schimbă abia după ce se adaugă ca membri. Restaurarea cere `tasks.archive` (pe endpoint, din token) și să vadă proiectul, ca un admin să restaureze fără să intre în proiect. Într-un proiect arhivat orice schimbare primește 409 `Project.Archived`.

**Regulile fiecărui endpoint** (task-uri, coloane și board, coloanele fixe, estimarea și pauza, timpul lucrat, „Arhivate”, setările generale, comentarii și atașamente, istoric, notificări, panoul principal) sunt în cod și în testele lui. Tabelul endpoint-urilor e în `backend/CLAUDE.md`, iar regulile de produs în `_Overview.md` și `Decisions/` din vault („Coloane fixe pe board”, „Timp lucrat în programul de lucru”). Două reguli de autorizare de ținut minte: coloanele le schimbă ownerul, `projects.manage` sau un membru real cu `columns.manage`, iar pe un task arhivat comentariile, fișierele și istoricul se văd doar cu `tasks.archive`.

**Un utilizator are exact un rol** (`users.role_id`, NOT NULL). Rolul grupează permisiuni (`role_permissions`). Dacă cineva are nevoie de drepturi combinate, adminul creează un rol nou — nu atribuim mai multe roluri aceleiași persoane. Schimbarea rolului invalidează refresh token-urile utilizatorului (permisiunile din JWT se reîmprospătează la următorul login/refresh).

Permisiunile ajung în JWT ca claims `permission`. Pe API: `[HasPermission(Permissions.ProjectsViewAll)]` — un `IAuthorizationPolicyProvider` custom construiește politica `Permission:<name>` la cerere, fără să le înregistrăm pe fiecare în `Program.cs`.

**Cine poate da ce.** Nimeni nu dă mai mult decât are. Un rol se creează, se editează sau se șterge doar de cineva care are toate permisiunile lui, cele vechi și cele noi (403 `Role.PermissionsBeyondYours`). Un utilizator primește, la creare sau la schimbarea rolului, doar un rol ale cărui permisiuni le are apelantul (403 `User.RoleBeyondYours`), iar nimeni nu editează, nu mută în alt rol și nu dezactivează pe cineva care are permisiuni în plus față de el (403 `User.PermissionsBeyondYours`). Altfel `users.manage` singur ar duce la `Admin`, iar `roles.manage` ar putea goli rolul cuiva aflat mai sus, ca apoi să-l dezactiveze. `GET /api/users/assignable-roles` întoarce rolurile pe care apelantul le poate da. Adminul are toate permisiunile, deci limita nu-l atinge.

**Cont dezactivat sau retrogradat.** Administrarea utilizatorilor și a rolurilor nu se încrede în token: `CurrentPermissions` recitește din baza de date utilizatorul și permisiunile lui la fiecare cerere (401 `Auth.SessionEnded` dacă e dezactivat, 403 `Auth.PermissionRequired` dacă permisiunea a dispărut, 403 `Auth.PasswordChangeRequired` dacă `MustChangePassword` e activ, de pildă după o resetare), iar dezactivarea și resetarea parolei închid sesiunile (refresh token-urile sunt revocate). Celelalte endpoint-uri se bazează pe claims din access token, deci un cont dezactivat, retrogradat sau cu parola resetată le mai poate apela până expiră token-ul, cel mult 15 minute (§4.5). Acceptat așa în v1, pe 2026-11-05.

**Reactivare și resetare de parolă.** Amândouă cer `users.manage` și respectă limita de mai sus (403 `User.PermissionsBeyondYours`). `POST /api/users/{id}/activate` readuce utilizatorul cu rolul și parola de dinainte, fără sesiune nouă. `POST /api/users/{id}/reset-password` primește o parolă inițială (politica din §4.5), pune `MustChangePassword = true`, golește `failed_login_attempts` și `locked_until` și revocă refresh token-urile, într-o singură salvare; pe un cont dezactivat e permisă și nu-l reactivează. Nimeni nu-și resetează propria parolă pe calea asta (403 `User.OwnPasswordReset`): `change-password` cere parola curentă tocmai ca un access token furat să nu poată prelua contul.

**Nivel proiect — rol pe proiect.** `ProjectMember(ProjectId, UserId, ProjectRole)` cu `ProjectRole = Owner | Member`. Verificat în handler prin `IProjectAccess`:
- `EnsureMemberAsync(projectId, userId)` — trece dacă e membru **sau** are `projects.view_all` **sau** `projects.manage` (cine poate administra orice proiect îl poate și vedea).
- `EnsureOwnerAsync(projectId, userId)` — trece dacă e owner **sau** are `projects.manage`.

Refuzul e un 403 ProblemDetails: `code: Auth.PermissionRequired` pentru o permisiune lipsă (numele permisiunii apare doar în log, la `Warning`, nu în răspuns), `Project.MemberRequired` / `Project.OwnerRequired` pentru rolul pe proiect, `Column.ManagerRequired` pentru cine nu poate schimba coloanele. `IProjectAccess` răspunde din baza de date, despre `userId`-ul primit, nu din claims: refuză utilizatorii dezactivați, nu verifică dacă proiectul există (handlerul răspunde 404 înainte) și păstrează owner-ii unui proiect arhivat.

**Owner dezactivat.** Dezactivarea unui cont nu se refuză pentru proiectele lui: un admin poate dezactiva pe cineva care pleacă fără să-i predea întâi fiecare proiect. Token-ul încă valid al unui owner dezactivat nu mai deschide proiectul (`IProjectAccess` refuză conturile dezactivate, 403), iar el rămâne în lista de membri cu chip-ul „Dezactivat”. „Mereu un owner” înseamnă un owner **activ**: `RemoveMember` și `ChangeMemberRole` numără doar ownerii activi care rămân, deci un owner nu poate ieși sau coborî la membru când singurul alt owner e dezactivat (409 `Project.LastOwner`); handlerul dă entității id-urile membrilor activi. Un proiect rămas fără owner activ prin dezactivare se administrează prin `projects.manage`, care adaugă întâi un owner nou.

Invariante:
- `Project` are mereu cel puțin un owner activ (verificat în `Project.RemoveMember` / `ChangeMemberRole`, cu id-urile membrilor activi de la handler; §4.4 „Owner dezactivat”). La cereri simultane ține `xmin`-ul proiectului: o schimbare a membrilor salvează și proiectul (`AuditingInterceptor`), deci din doi owneri care se scot unul pe altul în același timp al doilea primește 409 `Project.ChangedMeanwhile` și nu schimbă nimic (§5.3).
- Rolul `Admin` (`is_system = true`) nu poate fi șters, redenumit sau lipsit de permisiuni.
- Ultimul utilizator cu rolul `Admin` nu poate primi alt rol și nu poate fi dezactivat (verificat în handlerele `ChangeUserRole` și `DeactivateUser`, prin `LastAdminGuard`). O numărare nu ajunge când cererile se suprapun: `Role.HoldersVersion` crește de câte ori cineva pleacă din rol și e concurrency token, la fel `users.role_id` și `users.is_active`, deci a doua din două salvări simultane, sau una făcută pe o citire veche, primește 409 `User.ChangedMeanwhile`.
- Utilizatorii nu se șterg, se dezactivează (`is_active = false`): rămân referențiați din istoric, comentarii și rapoarte.

### 4.5 Autentificare

**Token-uri**

- `POST /api/auth/login` → access token JWT (15 min, în body) + refresh token (7 zile) în cookie `refresh_token`: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
- Access token-ul are claims `sub`, `permission` (câte unul pe permisiune) și `must_change_password`, doar când e cazul. E semnat HS256 și se validează fără toleranță de ceas.
- `POST /api/auth/refresh` rotește refresh token-ul (cel vechi devine invalid; refolosirea unuia invalidat revocă toată familia — detecție de furt). Două cereri simultane cu același token contează tot ca refolosire: `revoked_at` e concurrency token, deci a doua salvare nu mai găsește rândul.
- `POST /api/auth/logout` revocă familia sesiunii curente și șterge cookie-ul. Răspunde 204 și fără cookie.
- Login, refresh și logout sunt anonime, pentru că sunt tot ce-i rămâne unui client cu access token-ul expirat. Cookie-ul dovedește cine cheamă, iar `SameSite=Strict` îl ține departe de alte site-uri.
- Refresh token-urile se stochează hash-uite (SHA-256) în `refresh_tokens`. Curățarea: `RefreshTokensCleanupWorker` în Infrastructure, ca `OverdueTasksWorker` (după `DatabaseInitializer`, la pornire și apoi la fiecare `Auth:RefreshTokenCleanupInterval`, implicit o zi, cel puțin o oră; `Auth:RefreshTokenCleanupEnabled` false în testele API-ului), un singur `DELETE` pe rândurile expirate sau revocate de peste 30 de zile, cu numărul șters în log.
- Frontend ține access token-ul **doar în memorie** (semnal în `AuthStore`), niciodată în `localStorage`.
- SignalR primește token-ul prin query string `access_token` la handshake (standard), citit doar sub `/hubs` și doar când numele lui e în adresă exact cum a fost trimis (ASP.NET decodează `access%5Ftoken` în același nume, pe care redactarea din §8.1 nu l-ar vedea); sub `/api` nu deschide nimic. O conexiune se închide când îi expiră token-ul (`CloseOnAuthenticationExpiration`, `Close` cu `allowReconnect`), iar clientul se reconectează cu unul proaspăt, reîmprospătat când mai are cel mult 30 s, socotite din durata lui (`exp` minus `iat`) de la primire, nu din ceasul calculatorului, pe care API-ul, fără toleranță, l-ar prinde nesincronizat.
- Cookie-ul de refresh e al browserului, comun tab-urilor: când `sub`-ul token-ului primit la refresh nu e utilizatorul autentificat în tab (altcineva a intrat în cont în alt tab), tab-ul își încheie sesiunea.

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

- Handlere: `Result.Failure(Error)` cu `ErrorType ∈ { Validation, NotFound, Conflict, Forbidden, Unauthorized, Failure, Unavailable }`. `Unavailable` (grupul 25) e pentru un serviciu de care cererea are nevoie și care n-a răspuns, cum e modelul de vectorizare (§19.6): merită încercat din nou.
- Controllers: extensie `result.ToActionResult()` → 200/201/204 sau `ProblemDetails` cu 400/404/409/403/401/503.
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
                     ├──< Notification (overdue etc.)
                     └──< TaskEmbedding (vectorii, §19.2)
```

- `User`: `Email` (unic, păstrat cu litere mici), `DisplayName`, `PasswordHash`, `RoleId` (exact un rol; concurrency token, §4.4), `IsActive` (și el concurrency token, §4.4), `FailedLoginAttempts`, `LockedUntil?`, `MustChangePassword`.
- `Role`: `Name` (unic indiferent de majuscule, `ux_roles_lower_name`), `IsSystem` (true doar pentru `Admin`), `HoldersVersion` (crește când cineva pleacă din rol; concurrency token care ține invarianta ultimului `Admin`, §4.4), permisiuni prin `role_permissions`.
- `Project`: `Name`, `Key` (prefixul cheilor de task: 2–5 litere A–Z, scrise cu majuscule, unic și printre proiectele arhivate, `ux_projects_key`, ex. `KAN`; nu se schimbă după creare; 409 `Project.KeyTaken`), `Description?` (cel mult 2.000 de caractere, cu rânduri noi și tab-uri), `NextTaskNumber` (contorul din care se alocă numerele de task), `ArchivedAt?`, `ArchivedById?`; deține `Members` și `Columns` (la creare cele patru coloane fixe, Backlog / În lucru / În pauză / Gata, §4.4; până la grupul 22 erau De făcut / În lucru / Gata cu `is_done`).
- `TaskItem` (nu `Task`, ca să nu se bată cu `System.Threading.Tasks.Task`): `Number` (unic în proiect, alocat crescător la creare dintr-un singur `UPDATE projects SET next_task_number = next_task_number + 1 … RETURNING`, în tranzacția creării), `Title` (cel mult 300 de caractere), `Description` (markdown de bază, stocat ca text, cel mult 20.000 de caractere), `Priority` (enum `Low|Medium|High|Critical`), `DueDate?`, `Assignees` (oricâți membri activi ai proiectului, în tabela `task_assignees`), `ColumnId`, `Position`, `CompletedAt?`, `ArchivedAt?`, `ArchivedById?`, `EstimatedMinutes?` (15–60.000), `WorkedSeconds`, `WorkStartedAt?` și `PauseReason?` (cel mult 500 de caractere). Cheia afișată e `{Project.Key}-{Number}`, ex. `KAN-123`. Numărul rămâne și după arhivare, nu se refolosește.
- `BoardColumn`: `Name`, `Position`, `Kind` (`Backlog | InProgress | Paused | Done | Custom`, ca text; din grupul 22 în locul lui `IsDone`, §4.4 „Coloanele fixe”; intrarea unui task în coloana `Done` setează `CompletedAt`, ieșirea îl anulează). Fără WIP limit în v1.
- `TaskHistoryEntry`: `Field`, `OldValue`, `NewValue`, `ChangedById`, `ChangedAt`. Entitatea `TaskItem` o adaugă singură în metodele de modificare (`Rename`, `ChangeDescription`, `MoveTo`, `Assign`, `SetPriority`, `SetDueDate`, `Archive`, `Restore`) — istoricul e garantat, nu depinde de handler. O valoare care nu se schimbă nu scrie nimic, iar crearea nu scrie istoric.
- `Notification`: `UserId`, `Type` (enum `TaskOverdue|TaskAssigned|CommentAdded`), `TaskId?`, `Message`, `CreatedAt`, `ReadAt?`. Cine le produce e în §4.3: worker-ul pentru `TaskOverdue`, handlerele de domain events pentru celelalte două. Livrate exclusiv in-app (listă + clopoțel + push prin SignalR). **Nu** definim `IEmailSender` sau alt canal în v1; dacă apare nevoia, se adaugă atunci ca un consumer suplimentar al acelorași domain events.
- `WorkSchedule` (grupul 22): programul de lucru, un singur rând în `work_schedules`: `WorkDays` (zilele ISO, `work_days` ca text, „1,2,3,4,5”), `StartTime` și `EndTime` (`start_time`, `end_time`, de tip `time`), `UpdatedAt`, `UpdatedById?`; migrarea îl pune cu luni–vineri, 08:30–16:30 (§4.4 „Setările generale”).
- `TaskEmbedding` (grupul 25, `Domain/Search`): vectorul unui text din task, în `task_embeddings`: `TaskId` (cascadă: rândul pleacă odată cu task-ul), `Source` (enum `Task`, ca text; comentariile din pasul 6), `Content` (textul trimis la model), `Embedding` (în Domain o listă de `float`, în bază `vector` fără lungime fixă, prin conversia din configurația EF), `Model` (numele modelului, nu al deployment-ului), `EmbeddedAt`. Un rând pe task și sursă (`ux_task_embeddings_task_id_source`). `Replace` îl reface pentru alt text sau alt model. Distanța în LINQ: `EF.Property<Vector>(row, "Embedding").CosineDistance(intrebare)`, cu întrebarea ca `Vector` într-o variabilă locală (ca `float[]` ar pleca drept `real[]`, pe care `<=>` nu-l primește); §19.2.
- `EmbeddingCall` (grupul 25, `Domain/Search`): un apel la modelul de vectorizare, în `embedding_calls`, fără legătură cu alte tabele: `Purpose` (enum `Vectorization|Search`, ca text: vectorizarea task-urilor sau întrebarea unei căutări, §19.6), `Model` (numele modelului, ca lângă vectori), `TextCount` (cel puțin 1), `Tokens` (cât a numărat modelul pe tot apelul, cel puțin 0), `CalledAt`. Niciun text. Pentru calculatorul de costuri din pasul 4; §19.5.

**Arhivare, nu ștergere.** `DELETE /api/tasks/{id}` arhivează (`ArchivedAt = now`), nu șterge rânduri. Global query filter `HasQueryFilter(t => t.ArchivedAt == null)` pe `TaskItem`; comentariile, atașamentele și istoricul rămân intacte și intră în rapoarte. Admin (permisiunea `tasks.archive`) vede task-urile arhivate cu `?includeArchived=true` (handler cu `IgnoreQueryFilters()`) și le poate restaura (`POST /api/tasks/{id}/restore`). Nu există hard delete în API; curățarea fizică (dacă va fi vreodată nevoie) e o operațiune de DBA, nu o funcție de produs. Proiectele urmează aceeași regulă (`archived_at` pe `projects`).

### 5.1.1 Productivitate (feature `Reports`)

Nu e entitate; sunt trei queries read-only, filtrabile pe interval (`from`, `to`), pe proiect și pe utilizator. Pagina globală de rapoarte cere `reports.view`. Owner-ul unui proiect le vede fără permisiune, dar doar pentru proiectul lui, din tab-ul „Rapoarte” al paginii de proiect (UI §7.3): query-urile acceptă deci fie `reports.view`, fie owner pe `projectId`-ul din filtru, care devine obligatoriu în al doilea caz.

| Metrică | Definiție | Sursă |
|---|---|---|
| **Task-uri finalizate** | Număr de task-uri cu `completed_at` în interval, grupate pe asignat și pe proiect; serie pe săptămână/lună. Un task cu mai mulți asignați se numără la fiecare dintre ei, deci totalul pe oameni poate depăși totalul pe proiect, ceea ce pagina spune | `task_items.completed_at`, `task_assignees` |
| **Lead time / cycle time** | Lead: `completed_at − created_at`. Cycle: de la prima intrare a task-ului în „În lucru” până la `completed_at`; un task care n-a trecut prin ea pornește de la `created_at`. Plus timp mediu per coloană. Raportate ca medie și mediană | `task_history_entries` cu `field = 'ColumnId'` (reconstruim intervalele de ședere per coloană) |
| **Deadline** | Task-uri întârziate (`due_date < now` și neterminate), task-uri finalizate după deadline, rata de respectare = finalizate la timp / finalizate cu deadline | `due_date`, `completed_at` |

Toate exclud task-urile arhivate înainte de finalizare. Calculul se face în EF cu proiecții și `GroupBy`; dacă query-ul pe istoric devine lent, se materializează într-o tabelă `task_column_stays` (`task_id, column_id, entered_at, left_at`) întreținută din evenimentul `TaskMovedEvent` — decizie amânată până apar date reale.

Endpoint-urile sunt sub `/api/reports` (`completed`, `cycle-time`, `deadlines`, `work-time`), cu `from` și `to` obligatorii (cel mult 366 de zile), `projectId`, `userId` și `granularity`. Le citește cine are `reports.view`; fără ea, doar ownerul unui proiect, cu `projectId` dat. Clientul le arată în `features/reports/`, cu `kb-chart` din `shared/ui/` peste `chart.js`, încărcat cu `@defer` (UI §7.8).

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
- Concurență optimistă: `builder.Property<uint>("Version").IsRowVersion()` pe `TaskItem`, `Project`, `BoardColumn` (mutările pe kanban concurente se rezolvă cu 409). Npgsql o mapează pe `xmin`; `UseXminAsConcurrencyToken` nu mai există în Npgsql 10. Separat, `IsConcurrencyToken()` pe `Role.HoldersVersion`, `User.RoleId` și `User.IsActive` (invarianta ultimului `Admin`, §4.4) și pe `RefreshToken.RevokedAt` (rotirea refresh token-ului, §4.5). O schimbare a membrilor salvează și proiectul, iar una a asignaților și task-ul, ca `xmin`-ul lor să prindă cererile simultane (409 `Project.ChangedMeanwhile`, `Task.ChangedMeanwhile`).
- Crearea unui task e singura salvare dintr-o tranzacție deschisă explicit (`ITaskNumbers`, §4.2). `DomainEventsInterceptor` ține evenimentele unei salvări făcute într-o tranzacție deschisă până la prima salvare de după commit, pe care `TaskNumbers` o face imediat, goală, ca nimeni să nu afle de un task necomis.
- Enum-uri stocate ca `text` (`HasConversion<string>()`) — lizibile în DB și în Seq, sigure la reordonare.
- Toate datele în `timestamptz` (`DateTimeOffset` în C#). Serverul lucrează în UTC; frontend-ul afișează local.
- `string` → `varchar(n)` explicit cu `HasMaxLength`; `Description` și `Comment.Body` → `text`.
- Migrări în `Infrastructure/Persistence/Migrations/`, nume descriptive: `20261104120000_AddTaskHistory`. Generate cu `dotnet ef migrations add <Name> -p src/Kanabol.Infrastructure -s src/Kanabol.Api`.
- Colația: migrarea `AddRomanianCollation` pune colația ICU `ro-x-icu` a PostgreSQL-ului (vine din `initdb` când serverul e construit cu ICU, cum sunt imaginile oficiale și imaginea bazei, construită peste `postgres:17.11-alpine3.24` (§19.1), deci fără `CREATE COLLATION`) prin `UseCollation("ro-x-icu")` în configurările EF, pe coloanele de text după care se sortează: `task_items.title`, `users.display_name`, `users.email`, `projects.name` și `roles.name`. Orice `OrderBy` pe ele, inclusiv `upper(display_name)` din §4.4, ordonează atunci Ă, Â, Î, Ș și Ț la locul lor, fără `.Collate()` în query-uri; indexurile pe acele coloane se refac cu colația.
- Reziliență la conexiune: `EnableRetryOnFailure()` pe Npgsql, ca prima cerere după un restart al PostgreSQL să nu mai dea 500 (`57P01` dintr-o conexiune moartă din pool, văzut de `check-release.sh` 1 în grupul 19b). Tranzacția explicită din `TaskNumbers` (§4.2) trece prin `CreateExecutionStrategy().ExecuteAsync`, ca strategia să nu refuze tranzacțiile deschise de mână; restul salvărilor nu se schimbă.
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
    ├── settings/            # „Setări generale”: pagina programului de lucru, pentru settings.manage (grupul 22)
    └── reports/             # pagina de rapoarte (report-filters, durations, report-csv), încărcată și de ruta „Rapoarte” a proiectului (grupul 18)
```

Fiecare feature: `<feature>.routes.ts`, `<feature>-api.ts` (HTTP), `<feature>-store.ts` (state cu signals), `models.ts`, componente. Feature-urile nu se importă între ele direct; ce e comun urcă în `shared/`. Singura excepție e la nivel de rută: `projects.routes.ts` încarcă leneș (`loadComponent`) pagina de rapoarte din `features/reports/` pentru tab-ul „Rapoarte” al proiectului, cu proiectul fixat; componenta rămâne în chunk-ul ei, nu în bundle-ul inițial. Paginile placeholder (`PlaceholderPage` din `core/layout/pages.ts`, `features/projects/project-placeholder.ts`) au dispărut odată cu ultimele rute reale (grupul 18).

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
| **`@defer`** | Pentru widget-uri grele (rapoarte, editor de descriere). Ce nu e nevoie la pornire stă în chunk-uri lazy; bugetul bundle-ului inițial din `angular.json` e 750 kB (avertisment) și 1 MB (eroare) |
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
- **Un logger al API-ului, nu cel static** (grupul 25): `UseSerilog(..., preserveStaticLogger: true)`, iar request logging-ul primește explicit loggerul din DI, pentru că altfel ar scrie în `Log.Logger`, care rămâne gol. Cu loggerul static, al doilea API pornit în același proces, cum fac testele căutării, lua logurile primului.
- **Niveluri**:

| Sursă | Dev | Prod |
|---|---|---|
| Default | Debug | Information |
| `Microsoft.AspNetCore` | Information | Warning |
| `Microsoft.EntityFrameworkCore.Database.Command` | Information (vezi SQL-ul) | Warning |
| `Kanabol.*` | Debug | Information |

- **Ce logăm**: fiecare command executat (nume, durata, succes/eșec — din `LoggingDecorator`), autentificări reușite/eșuate (cu `UserId`/email, fără parolă), permisiuni refuzate, job-urile de alerte (câte task-uri procesate), erori neașteptate cu stack trace.
- **Ce NU logăm**: parole, token-uri (nici hash-ul), conținutul atașamentelor, body-ul complet al request-urilor. Dacă e nevoie de debugging pe body, se activează explicit și temporar în dev. Token-ul din query string-ul hub-ului ar ajunge în „Request starting” al ASP.NET (Development scrie adresa întreagă) și în orice intrare cu adresa: `AccessTokenRedaction`, un enricher Serilog, ascunde valoarea lui `access_token` în orice proprietate a unei intrări, iar logul clientului SignalR trece prin `Logger` cu token-ul ascuns; nginx nu ține access log pe `/hubs/` (§13.2), error log-ul lui și redirect-ul de pe portul 80 rămân cum sunt. Redactarea intră și în valorile structurate (liste, obiecte, dicționare), recursiv.
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
- Limita de 10 intrări pe minut e în browser: ce trece peste ea se numără și vine ca `dropped` în batch-ul următor, pe care serverul îl loghează ca warning. Serverul nu numără cererile pe minut; primește cel mult 10 intrări într-un batch, doar `warn` și `error`, cu lungimi limitate (mesaj 4.000, URL 2.000, detalii 16.000, correlation id 100 de caractere) și răspunde 204. O limită pe adresă la server se adaugă doar dacă apare nevoia.
- Trimite doar un utilizator autentificat, ca orice controller, și nu cât timp `MustChangePassword` e activ: erorile din pagina de login și din schimbarea obligatorie a parolei rămân în consola browserului, nu ajung în Seq. Browserul nici nu trimite fără sesiune, ca un 401 să nu-l ducă la login.
- `GlobalErrorHandler` (`ErrorHandler` Angular): prinde erorile neprinse → `Logger.error` + snackbar generic.
- Fără `console.log` în cod comis (regulă ESLint `no-console: ["error", { allow: ["warn", "error"] }]`; Logger-ul are excepție).

---

## 9. Real-time (SignalR)

- Hub `BoardHub` la `/hubs/board`, autorizat cu JWT, doar peste WebSockets: la long polling și la Server-Sent Events fiecare cerere poate aduce un token proaspăt și muta expirarea, deci cine era scos dintr-un proiect l-ar auzi fără sfârșit. Clientul apelează `JoinProject(projectId)` → grup `project:{id}`; serverul verifică accesul prin `IProjectAccess` (membru, `projects.view_all` sau `projects.manage`, cont activ; un proiect inexistent nu e căutat), iar `LeaveProject(projectId)` îl scoate din grup. Un refuz e răspunsul cu codul `Project.MemberRequired`, nu o excepție, ca SignalR să nu-l scrie ca `Error`, și un warning în log; join-urile și refuzurile se loghează cu `UserId`, `ProjectId` și `ConnectionId` (§8.2).
- Evenimente către client: `TaskCreated`, `TaskMoved`, `TaskUpdated`, `TaskArchived`, `TaskRestored`, `CommentAdded` (grup `project:{id}`), `NotificationCreated` (grup `user:{id}`, payload `{ notificationId }`, pentru oricine a primit notificarea, oricine a făcut-o; conexiunea intră automat în grupul propriu; alimentează clopoțelul din navbar; decis 2026-11-07).
- Sursa: domain events → `IDomainEventHandler<TaskMovedEvent>` în Application → `IBoardNotifier` (interfață în Application, implementată în Api peste `IHubContext<BoardHub>`). Handlerele de domeniu nu știu de SignalR. `TaskAssignedEvent` trimite și el `TaskUpdated`, cu `field = Assignees`, pentru că schimbarea asignaților nu ridică `TaskUpdatedEvent`; `CommentAdded` vine din `CommentAddedEvent` (grupul 15), cu `commentId` pe lângă câmpurile comune, iar un atașament urcat sau șters trimite `TaskUpdated` cu `field = Attachments`, pentru numărul de pe card; board-ul se recitește în liniște la amândouă, iar panoul deschis nu ascultă nici acum.
- Payload-uri mici: fiecare eveniment poartă `taskId`, `projectId`, `byUserId` și `originConnectionId`; `TaskMoved` și `columnId` și `position`, ca board-ul să mute cardul local cu animația din UI §7.4; `TaskUpdated` și `field` (`Title`, `Description`, `Priority`, `DueDate`, `Assignees`, `Attachments` sau, din grupul 22, `EstimatedMinutes`). Din grupul 23, orice schimbare a coloanelor unui proiect (adăugare, redenumire, mutare, ștergere) trimite grupului proiectului `ColumnsChanged`, cu `projectId`, `byUserId` și `originConnectionId`, iar board-urile se recitesc în liniște, ca la `TaskUpdated`. La `TaskCreated`, `TaskUpdated`, `TaskArchived` și `TaskRestored` board-ul se recitește în liniște, cu un `GET /api/projects/{id}/board`, și pulsează cardul schimbat; la reconectare face re-join și se recitește, pentru ce a pierdut. O creare cu asignați trimite `TaskCreated` și `TaskUpdated` cu `Assignees`, deci celelalte board-uri se recitesc de două ori, acceptat. În v1 ascultă doar board-ul, nu Lista și nu panoul.
- Propria conexiune: clientul pune id-ul conexiunii lui în header-ul `X-Connection-Id` pe fiecare cerere API; un context de cerere scoped în Application, lângă `ICurrentUser`, îl dă handlerelor, care îl pun în payload ca `originConnectionId`, iar `BoardStore` ignoră un eveniment doar când `originConnectionId` e al tab-ului lui și `byUserId` e utilizatorul autentificat: id-urile conexiunilor se văd în payload-uri de toți membrii proiectului, deci un id luat de la altul nu ascunde nimic. Header-ul trece doar ca id de SignalR (litere, cifre, `-` și `_`, cel mult 64 de caractere, trimis o dată), altfel e nul. Fără conexiune header-ul lipsește, `originConnectionId` e nul și nimeni nu ignoră nimic.
- Client: `Realtime` service (`core/realtime`) cu `HubConnectionBuilder`, reconectare automată, re-join la reconectare; store-urile se abonează. Conexiunea se deschide la primul board deschis și ține cât sesiunea; SignalR stă în chunk-ul board-ului (`TabConnection` ține id-ul pentru interceptor), ca bundle-ul inițial să nu crească. Clopoțelul (grupul 17) cheamă `Realtime` după autentificare, ca grupul propriu să existe și fără board deschis, printr-un `import()` dinamic al modulului (`Realtime.open()`, conexiunea fără proiect; grupul 17), ca SignalR să rămână în afara bundle-ului inițial, în chunk-ul pe care îl împarte cu board-ul; la `NotificationCreated` și la reconectare clopoțelul recitește numărul necitit și, dacă meniul e deschis, lista; la ieșirea din cont uită tot.
- Limite: cel mult 8 proiecte pe conexiune (`Hub.TooManyProjects`), iar după 5 refuzuri conexiunea se închide. Scoaterea din proiect scoate conexiunile omului din grupul proiectului, iar schimbarea rolului și dezactivarea i le închid (`IRealtimeAccess`, chemat din handler-e). Trimiterile trec printr-un canal cu un singur consumator (`BoardOutbox`, citit de `BoardSender`), ca ordinea să rămână și cererea care a făcut schimbarea să nu aștepte socket-ul nimănui.

---

## 10. Atașamente

- `IFileStorage { SaveAsync(stream, fileName, ct) → StoredFile(Key, Size, ContentType); OpenReadAsync(key); DeleteAsync(key) }`.
- Implementare v1: `LocalFileStorage` → `/data/attachments/{yyyy}/{MM}/{guid}{ext}` (volum Docker). Metadatele (`FileName`, `ContentType`, `Size`, `StorageKey`, `UploadedById`) în `attachments`.
- Limite: 25 MB/fișier (25 × 1.048.576 de octeți), listă albă de extensii din `Storage:AllowedExtensions` (implicit pdf, png, jpg, jpeg, gif, txt, md, csv, xlsx, docx, zip), numele original nu ajunge niciodată pe disc. Lista e o singură valoare cu virgule, `Storage__AllowedExtensions=pdf,png` în compose, care înlocuiește toată lista implicită; extensiile se iau o dată fiecare, fără spații și fără punct, în litere mici, în ordinea dată. API-ul nu pornește fără nicio extensie, nici cu o listă pe indici (`Storage__AllowedExtensions__0=exe`, forma de dinaintea grupului 16), pe care legarea la o singură valoare ar ignora-o în tăcere; mesajul de la pornire dă forma cea nouă. Urcă un membru real pe un task deschis, un fișier pe cerere, prin `POST /api/tasks/{id}/attachments`, în câmpul `file`; `DELETE /api/attachments/{id}` șterge rândul și apoi fișierul, pentru cine l-a urcat sau pentru `tasks.archive` (§4.4; decis 2026-11-07).
- Refuzul e 400 pe `file`: fără fișier, cu două, în alt câmp, cu un nume gol, de peste 255 de caractere sau cu caractere de control, peste limită sau cu o extensie din afara listei. Numele păstrează doar ce vine după ultimul `/` sau `\`; extensia e ce vine după ultimul punct, în orice majuscule (`arhiva.tar.gz` e `gz`). Cererea poate avea cu 1 MB mai mult decât fișierul; peste asta API-ul nu citește formularul și răspunde același 400 (nginx oprește peste 30 MB cu 413, §13.2). Tipul conținutului vine din extensie, nu de la browser. Un fișier rămas pe disc după ce rândul lui a dispărut e un warning în log.
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

Fișierul e în rădăcina repo-ului: `.editorconfig`.

### 12.2 Backend — `Directory.Build.props`

Fișierul: `backend/Directory.Build.props`.

Verificare: `dotnet format --verify-no-changes` (stil) + `dotnet build -warnaserror` (analizoare). Fără StyleCop — analizoarele .NET + `.editorconfig` sunt suficiente și nu necesită pachet extra. În `Kanabol.Domain` și `Kanabol.Application` adăugăm regulă de arhitectură prin test (`NetArchTest.Rules`): `Domain` nu referă `Microsoft.EntityFrameworkCore`; `Application` nu referă `Infrastructure`.

### 12.3 Frontend — ESLint + Prettier

Regulile ESLint sunt în `frontend/eslint.config.js` (flat config).

`.prettierrc`: `{ "printWidth": 100, "singleQuote": true, "trailingComma": "all", "overrides": [{ "files": "*.html", "options": { "parser": "angular" } }] }`.

`tsconfig.json`: `strict: true`, `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`, `angularCompilerOptions.strictTemplates: true`.

---

## 13. Docker

### 13.1 `backend/Dockerfile`

Fișierul: `backend/Dockerfile`.

Restore separat de sursă → cache-ul Docker nu reface restore la fiecare schimbare de cod. Testele **nu** rulează în Dockerfile (rulează în hook); imaginea e doar runtime. Tot în hook rulează și analizoarele: la publish sunt oprite, pentru că regulile lor stau în `.editorconfig` din rădăcină, în afara contextului de build. Imaginea `aspnet` n-are nici `wget`, nici `curl`, așa că `wget` se instalează pentru healthcheck.

### 13.2 `frontend/Dockerfile`

Fișierul: `frontend/Dockerfile`.

nginx are trei fișiere. Ce au comun dev și prod stă în `nginx-app.conf`, inclus din `nginx.conf` (dev) și din `nginx.prod.conf`, pe care `compose.prod.yaml` îl montează peste `nginx.conf`. Headerele stau la nivel de server în `nginx-app.conf`, nu în `location`-uri, unde `add_header` le-ar pierde pe cele de sus: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` fără cameră, microfon și geolocation, și CSP `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'` (Angular și Material injectează stiluri inline; un nonce ar cere `sub_filter` în nginx și rămâne pentru mai târziu); HSTS `max-age=31536000`, fără preload, doar în `nginx.prod.conf`, pe 443. Fiecare `add_header` are `always`, ca și paginile de eroare să le aibă, iar build-ul de producție are `optimization.styles.inlineCritical: false`, pentru că CSP-ul blochează scriptul inline al inlinierii.

Fișierele sunt în `frontend/`: `nginx-app.conf`, `nginx.conf` și `nginx.prod.conf`.

Același origin pentru SPA și API (prin nginx) → fără CORS în prod, cookie-ul de refresh funcționează cu `SameSite=Strict`.

Un login trimis prin calea hub-ului (`/hubs/#/../../api/auth/login`, cu ținta lăsată cum e scrisă) nu ocolește limita de încercări din §4.5: răspunsul e 429, de la limită, sau 400, când nginx sau API-ul refuză ținta cu `#` înainte de orice login (pe mașinile GitHub; pe mașina de lucru răspunsul e 429). `check-docker.sh` 2 ia oricare dintre cele două și pică pe 401 sau pe orice altceva, adică pe un login trecut pe acolo.

### 13.3 Compose

`compose.yaml` (comun):

```yaml
services:
  postgres:
    build: ./database
    image: kanabol-postgres:17.11-pgvector0.8.7
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
      # The work schedule's time zone is Europe/Bucharest by default: Schedule__TimeZone=Europe/Vienna changes it (group 22)
      Database__MigrateOnStartup: "true"
      # Vectorization, for the search by meaning, is off unless EMBEDDINGS_ENABLED=true in .env, which then needs the four after it (docs/SPEC.md §19.4, group 25)
      Embeddings__Enabled: ${EMBEDDINGS_ENABLED:-false}
      Embeddings__Model: ${EMBEDDINGS_MODEL:-}
      Embeddings__Azure__Endpoint: ${EMBEDDINGS_AZURE_ENDPOINT:-}
      Embeddings__Azure__Deployment: ${EMBEDDINGS_AZURE_DEPLOYMENT:-}
      Embeddings__Azure__ApiKey: ${EMBEDDINGS_AZURE_API_KEY:-}
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
# It runs tagged images and builds none: scripts/release.sh builds all three. KANABOL_VERSION picks another
# tag than the default one of api and web.
# The default is the version of this commit: the commit a version tags sets it here, on both images, and
# in APP_VERSION (frontend/src/app/core/layout/sidenav.ts), docs/SPEC.md §13.5. The database's tag is
# compose.yaml's.
services:
  postgres:
    build: !reset null
    restart: unless-stopped

  seq:
    restart: unless-stopped

  api:
    build: !reset null
    image: kanabol-api:${KANABOL_VERSION:-v1.0.0}
    restart: unless-stopped

  web:
    build: !reset null
    image: kanabol-web:${KANABOL_VERSION:-v1.0.0}
    restart: unless-stopped
    ports: ["80:80", "443:443"]
    volumes:
      - ./certs:/etc/nginx/certs:ro
      - ./frontend/nginx.prod.conf:/etc/nginx/conf.d/default.conf:ro
```

Expune doar `web`, pe 443 și pe 80 ca redirect către HTTPS, fără niciun build, nici al bazei, cu imagini tag-uite: `release.sh` le construiește pe toate trei (§13.5). `KANABOL_VERSION` alege alt tag decât cel implicit, care e versiunea commit-ului (§13.5; `v0.1.2` la grupul 21, `v1.0.0` din 2026-11-10); `.env.example` nu mai numește o versiune.

Seq își păstrează retenția în volum, deci fiecare volum nou are nevoie o dată de `SEQ_ADMIN_PASSWORD=<parola> scripts/seq-retention.sh` (§8.1).

În producție Seq n-are port publicat: `seq-retention.sh` rulează de pe server la adresa containerului, iar interfața se vede printr-un tunel SSH; `docs/deploy.md` dă comenzile. După un restart al PostgreSQL API-ul reia singur conexiunile moarte din pool (§5.3, grupul 20); `docs/deploy.md` nu cere repornirea lui `api`, iar `check-collation.sh` 2 verifică că n-o cere.

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

### 13.5 Livrare

- `scripts/backup.sh`, rulat pe server din cron: `pg_dump -Fc` prin `docker compose exec`, plus un tar al volumului de atașamente printr-un container alpine, în `backups/kanabol-AAAA-LL-ZZ/`; păstrează 14 zile; nu scrie nicio parolă în log. `scripts/restore.sh` întoarce un backup într-un stack oprit. `docs/backup.md` dă linia de cron și pașii restaurării.
- `scripts/release.sh vX.Y.Z` construiește și tag-uiește `kanabol-api` și `kanabol-web` (§13.3) și pune tag-ul git. Construiește și imaginea bazei, `kanabol-postgres`, cu tag-ul din `compose.yaml` (§19.1), dar doar când s-a schimbat `database/`, pe care îl recunoaște după eticheta cu arborele git al folderului. Altfel fiecare construire ar da o imagine nouă, iar `compose up` ar reporni PostgreSQL la fiecare versiune. `docs/deploy.md` dă pașii serverului: `.env`, `make-certs.sh`, `compose up`, `seq-retention.sh`, cron-ul de backup, `ca.crt` după `docs/tls.md`, actualizarea la o versiune nouă. Instalarea pe server, `ca.crt` la colegi și `compose up` sunt ale lui Florin, după `docs/deploy.md`.
- Verificarea UI (UI §15): capturi ale fiecărui ecran din UI §7, light și dark, la 1440×900, PNG în `docs/screens/` (`7.2-panou-light.png`), dintr-o bază cu date de probă; contrastul cu `axe-core` (dependență de dev) pe fiecare ecran, în ambele teme, cu rezultatele și reparațiile în `docs/ui-review.md`; `prefers-reduced-motion` verificat că oprește tranzițiile și shimmer-ul; ce pică și nu se repară pe loc intră în Blocked cu propunere.
- Fără registry de imagini: serverul are clona repo-ului, iar `release.sh vX.Y.Z` rulează pe el după `git pull`. Pașii sunt în `docs/deploy.md` și `docs/backup.md`; datele demonstrative vin din `scripts/demo-data.sh`, prin API.
- Versiunea: commit-ul pe care îl etichetează o versiune pune acea versiune în `APP_VERSION` (`frontend/src/app/core/layout/sidenav.ts`) și în tag-ul implicit al ambelor imagini din `compose.prod.yaml`; `release.sh` refuză un tag care nu se potrivește cu ele. Capturile din `docs/screens/` nu se refac pentru un număr de versiune.

---

## 14. Pipeline la commit și CI

Cerință: hook-urile rulează **aceleași verificări rapide pe orice branch**, `main` inclus, pe mașina dezvoltatorului. Suita întreagă (`scripts/test-all.sh`) și gate-ul întreg (§14.7) rulează în workflow-ul GitHub Actions al repo-ului (`docs/ci.md`); local, suita se rulează de mână. Până la v1.33 (2026-11-12) suita întreagă rula în hook-uri la commit, la merge și la push pe `main`, cu Docker Desktop pornit; Florin a mutat-o în CI, pentru că un merge în `main` ținea minute întregi și pica pe mașina de lucru sub încărcare.

Branch-uri: fiecare task se lucrează pe un branch `feature/<kebab>` (sau `fix/`, `chore/`, `docs/`, §7.5) pornit din `dev`. Când un grup din `Tasks.md` e gata și treapta scurtă din CI e verde pe branch-ul lui (§14.7), branch-ul intră în `dev`; aici fast-forward e permis, pentru că fiecare commit a trecut deja prin `pre-commit`. `dev` intră în `main` mereu cu `--no-ff`, ca merge commit-ul să treacă prin hook, iar **push-ul lui pe `main` rulează gate-ul întreg pe GitHub** (§14.7; decis 2026-11-13: gate-ul întreg nu rulează altundeva și nu se pornește de mână înainte de merge). Un tag de versiune se pune doar pe un `main` al cărui gate e verde. `main` conține deci doar grupuri terminate, iar `dev` e locul unde se adună munca dintre două merge-uri în `main`.

### 14.1 Tooling

`package.json` din rădăcina repo-ului e doar pentru hook-uri (husky, lint-staged, commitlint, prettier); nu e al frontend-ului.

`npm install` la rădăcină (rulat de `scripts/setup.sh`) instalează hook-urile din `.husky/`. Hook-urile sunt în repo, deci orice clonă le primește. `prettier` e aici pentru Markdown și YAML din rădăcină, iar `.prettierignore` îl ține departe de `docs/SPEC.md` și de copiile din vault (`docs/UI_UX_SPEC.md`, `docs/adr/`).

### 14.2 Hook-uri

Hook-urile sunt în `.husky/`: `pre-commit` și `pre-merge-commit` rulează `scripts/verify.sh` (`git merge --no-ff` creează un commit, dar nu cheamă `pre-commit`), iar `commit-msg` rulează commitlint.

Nu există hook `pre-push` (scos în v1.33): plasa de siguranță pentru un commit făcut cu `--no-verify` e gate-ul pe care workflow-ul îl rulează la push pe `main` (§14.7).

### 14.3 `scripts/verify.sh`

Scriptul: `scripts/verify.sh`. Rulează lint-staged pe fișierele din index, apoi, după ce atinge commit-ul, build-ul backend-ului cu testele de domeniu, specurile frontend-ului sau amândouă, în paralel.

La un merge, `git diff --cached` are tot ce aduce merge-ul, deci un merge al lui `dev` în `main` construiește backend-ul și rulează testele de domeniu dacă aduce ceva din `backend/`, și specurile dacă aduce ceva din `frontend/`. Testele de Application și Api, care au nevoie de Docker, rulează doar în suita întreagă. `scripts/require-docker.sh` nu mai e chemat de niciun hook: rămâne pentru rularea suitei de mână (`scripts/require-docker.sh && scripts/test-all.sh`), unde oprește cu un mesaj clar când Docker e oprit.

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

`.lintstagedrc.json` din rădăcină formatează C#-ul cu `dotnet format` și fișierele `md` și `yaml` cu prettier; `frontend/.lintstagedrc.json`, separat, rulează eslint și prettier din `frontend/`, cu configurațiile de acolo.

### 14.6 Reguli de git care fac hook-urile de încredere

`scripts/setup.sh`, rulat după clone (se poate rula din nou), instalează pachetele, pornește hook-urile și setează `branch.main.mergeOptions` pe `--no-ff`.

Observații:
- **Fast-forward nu rulează niciun hook** — de aceea forțăm `--no-ff` pe `main`. Merge commit-ul trece prin `pre-merge-commit` → `verify.sh` → verificările rapide ale lui `main`, iar push-ul lui pornește gate-ul în workflow. Pe `dev` nu forțăm nimic: un merge fast-forward acolo aduce doar commit-uri care au trecut deja prin `pre-commit`.
- `core.hooksPath` rămâne `.husky/_`, cum îl setează husky 9; nu-l suprascriem.
- Testele de integrare au nevoie de **Docker** (Testcontainers), deci doar suita întreagă: în workflow, unde Docker e pe mașină, și de mână, cu Docker Desktop pornit. Hook-urile nu mai au nevoie de Docker pe niciun branch.
- `git commit --no-verify` rămâne posibil (git nu poate interzice); gate-ul din workflow, la push pe `main`, e plasa de siguranță.
- Durata: pe orice branch, `main` inclus, limita e 60 s: la primele măsurători un commit care atinge și backend, și frontend dura 21–22 s, unul pe o singură parte 14–18 s (`check-pipeline.sh` 6 o verifică pe commit-uri reale). Limita a fost 40 s până la v1.34: cu suita de acum, un commit pe ambele părți ține 36 s pe mașina de lucru liniștită, 42–46 s pe una încărcată și 45 s pe mașinile GitHub, deci stătea la marginea ei; `check-pipeline.sh` 6 cere 60 s. De la v1.33 cele două părți rulează în paralel (§14.3): pe mașinile GitHub, unul după altul, commit-ul pe ambele părți ținea 45–46 s. Limita merge-ului în `main` (90 s pe 2026-11-06, 150 s pe 2026-11-07, 180 s pe 2026-11-09, 210 s pe 2026-11-10) a dispărut în v1.33, odată cu suita din hook-uri: merge-ul face acum aceleași verificări rapide. Ultimele măsurători, cu suita în hook: 142 s în gate și 179 s la merge-ul real după grupul 21 (d2439a3).
- Pe Windows, hook-urile rulează prin `sh` din Git for Windows; scripturile sunt POSIX sh (fără bash-isme) și au `eol=lf` prin `.gitattributes`.

### 14.7 Verificări de acceptanță

Fiecare grup din `Tasks.md` are verificările lui în `scripts/check-*.sh`. Nu rulează în hook-uri. Rulează în workflow-ul GitHub (`docs/ci.md`), pe o clonă curată, în două trepte. Local nu se cere nicio rulare: treapta locală a grupului a ieșit pe 2026-11-13, pentru că repeta pe mașina de lucru, cu Docker, ce face CI-ul la fiecare push.

- **Treapta scurtă**, la fiecare push pe `dev` sau pe un branch de lucru: `check-tests.sh`, `check-lint.sh` și fiecare `check-*.sh` schimbat de push (§14.8). Un grup e gata și intră în `dev` când treapta scurtă e verde pe ultimul lui commit. Un script atins de grup, dar neschimbat, rulează abia la gate-ul întreg.
- **Gate-ul întreg**: o clonă proaspătă, `scripts/setup.sh` și toate scripturile `check-*.sh`. Rulează în workflow-ul GitHub (`docs/ci.md`), nu local, și doar la push pe `main`, adică atunci când `dev` intră în `main`, pe merge commit (decis 2026-11-13: fără pornire de mână înainte de merge, fără `[full gate]` și fără o regulă aparte până la prima rulare verde). Un tag de versiune (§13.5) se pune doar pe un `main` al cărui gate e verde; un gate roșu se repară în `dev`, care intră din nou în `main`.

Contraprobele din capul unui script le rulează de mână cine schimbă scriptul, când îl schimbă.

Gate-ul întreg îl rulează `scripts/gate.sh` (local `scripts/gate.sh --fresh <branch> <clonă> <loguri>`, în CI pe bucăți, `docs/ci.md`). Munca pe care scripturile o repetau (build-ul, formatarea, lint-ul, build-ul de producție și suita întreagă) o face o dată `scripts/gate-prepare.sh`; un script o refolosește doar când gate-ul i-o cere (`KANABOL_GATE_CACHE`), pentru aceeași comandă, pe același arbore. Contraprobele, API-ul real și Docker-ul rulează mereu de-adevăratelea.

- `check-tests.sh`, `check-auth.sh`, `check-permissions.sh`, `check-users.sh`, `check-admin.sh`, `check-projects.sh`, `check-tasks.sh`, `check-board.sh`, `check-assignees.sh`, `check-realtime.sh`, `check-comments.sh`, `check-history.sh`, `check-alerts.sh`, `check-dashboard.sh`, `check-reports.sh`, `check-hardening.sh`, `check-search.sh`, `check-release.sh` și `check-docker.sh` au nevoie de Docker pornit (`check-pipeline.sh` nu, de la v1.33).
- `check-backend.sh`, `check-auth.sh`, `check-permissions.sh`, `check-users.sh`, `check-admin.sh`, `check-projects.sh`, `check-tasks.sh`, `check-board.sh`, `check-assignees.sh`, `check-realtime.sh`, `check-comments.sh`, `check-history.sh`, `check-alerts.sh`, `check-dashboard.sh`, `check-reports.sh`, `check-hardening.sh` și `check-search.sh` au nevoie de un PostgreSQL cu extensia `pgvector` instalată (migrarea `AddVectorExtension` o activează, §19.1), al cărui utilizator poate crea baze de date, din `KANABOL_CHECK_DB` sau din user secret-ul `ConnectionStrings:Default`. Lucrează în baze proprii și le șterg la final.
- `check-docker.sh 5` are nevoie de porturile 5432, 5341 și 5200 libere.
- Ce verifică fiecare task al unui script și de ce are nevoie (Docker, PostgreSQL, Node sau doar pachetele) scrie în capul scriptului.

### 14.8 CI (GitHub Actions)

`.github/workflows/ci.yml` rulează exact `scripts/test-all.sh` și scripturile `check-*.sh`, fără duplicare de logică; `docs/ci.md` spune ce rulează când și cum se citește un eșec.

- **Când**: la push pe `dev`, `feature/*`, `fix/*` și `chore/*` treapta scurtă: `check-tests.sh`, `check-lint.sh` și fiecare `check-*.sh` schimbat de push (scriptul grupului nu se poate ghici în CI; pentru un branch nou, față de `dev`); la push pe `main`, și doar atunci, gate-ul întreg (decis 2026-11-13; fără pornire de mână prin `workflow_dispatch` și fără `[full gate]` în subiectul ultimului commit, cu care gate-ul a rulat pe `chore/gate-speed`). `scripts/ci-plan.sh` alege și împarte.
- **Cum, pe ce mașină și cum se citește un eșec**: în `docs/ci.md`. Pe scurt: doar `ubuntu-latest` (repo-ul e public, deci gratuit), un job `prepare` care face o dată munca comună și joburi `check (…)` în paralel, prin `scripts/gate.sh`.
- **Siguranța**: doar acțiunile oficiale (`actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/cache`, `actions/upload-artifact`), fixate pe commit, cu versiunea în comentariu; permisiunea `contents: read`; niciun secret: logurile sunt publice, iar valorile de test vin din scripturi și `.env.example` sau sunt generate în job.
- **Limitele de timp** din scripturi (commit-urile sub 60 s din `check-pipeline.sh` 6, §14.6) rămân cum sunt; dacă nu țin pe mașinile GitHub, timpul măsurat și o propunere merg în Blocked.

---

## 15. Configurare și secrete

- `appsettings.json` (valori comune) + `appsettings.Development.json` (loguri verbose, Swagger). Fără CORS: în dev `proxy.conf.json`, în prod nginx — același origin peste tot.
- Secrete în dev: `dotnet user-secrets` (`Jwt:Secret`, `ConnectionStrings:Default`, `Admin:InitialPassword` și, doar cu vectorizarea pornită, `Embeddings:Azure:Endpoint`, `Embeddings:Azure:Deployment`, `Embeddings:Azure:ApiKey`, §19.4). Override-uri locale **nesecrete** pot sta în `appsettings.Development.local.json` (deja în `.gitignore`), încărcat explicit în `Program.cs` după cel standard. În Docker/prod: variabile de mediu (`__` ca separator).
- Toate opțiunile tipate (`JwtOptions` etc.) cu `[Required]` și `ValidateOnStart()` → aplicația refuză să pornească cu config incompletă, nu eșuează la primul request.
- Frontend: `environment.ts` conține doar `apiBaseUrl` (`/api`) și `hubsBaseUrl` (`/hubs`); același build pentru toate mediile, nginx face restul.

---

## 16. Securitate de bază (checklist v1)

- [x] HTTPS terminat în nginx cu CA proprie + certificat self-signed (§13.4); `ca.crt` importat pe stațiile colegilor; HTTP doar redirect. Verificat: `check-docker.sh` (TLS și redirectul de pe 80) și `check-hardening.sh` 1; `ca.crt` la colegi e al lui Florin, la instalare (grupul 19b, 2026-11-09).
- [x] `HSTS`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` și CSP-ul din §13.2 (grupul 19; decis 2026-11-09). Verificat: `check-hardening.sh` 1 (grupul 19b, 2026-11-09).
- [x] Rate limiting pe login; lockout 15 min după 10 eșecuri consecutive (câmp `locked_until` pe `users`). Verificat: testele de auth și `check-auth.sh` pe API-ul real (grupul 19b, 2026-11-09).
- [x] Parole: minim 10 caractere, verificate contra unei liste scurte de parole comune. Verificat: `check-auth.sh` (lista `common-passwords.txt` și refuzul unei parole comune) (grupul 19b, 2026-11-09).
- [x] Refresh token rotit, revocare la logout, la schimbarea parolei, la dezactivare și la resetarea parolei de către admin. Parola proprie se schimbă doar cu cea curentă, niciodată prin endpoint-ul de resetare (§4.4). Verificat: testele de auth și `check-auth.sh` (rotirea, logout-ul, schimbarea parolei), `check-users.sh` (dezactivarea, resetarea de către admin, parola proprie), `check-realtime.sh` (un refresh pentru altcineva închide sesiunea) (grupul 19b, 2026-11-09).
- [x] Validare de input în toate commands cu date de verificat (FluentValidation); EF parametrizat → fără SQL injection. Verificat o dată, de mână (grupul 19b, 2026-11-09): 17 commands n-au validator și toate au doar id-uri (legate tipat, deci 400 la unul greșit), nimic sau un deadline trecut în UTC; singurul SQL scris de mână din `backend/src` e în `TaskNumbers`, interpolat, deci cu parametri. `check-release.sh` 5 (grupul 20) verifică validatorii de fiecare dată.
- [x] Atașamente: extensii permise, dimensiune max, nume generat, `Content-Disposition: attachment`, nu se execută niciodată din `wwwroot`. Verificat: `check-comments.sh` 2; API-ul nu servește fișiere statice (nici `UseStaticFiles`, nici `wwwroot`), atașamentele stau în `Storage:Root`, în afara rădăcinii lui nginx, verificat de mână (grupul 19b, 2026-11-09).
- [x] Backend verifică permisiunea și accesul la proiect la fiecare request; UI doar ascunde. Verificat: `check-permissions.sh`, `check-users.sh` și „un străin primește 403” în scriptul fiecărui grup (grupul 19b, 2026-11-09).
- [x] Markdown-ul din descrieri și comentarii se randează în client prin parserul propriu (`shared/ui/markdown-parser.ts`, `kb-markdown`), care citește textul într-un arbore randat prin template-uri, deci nimic nu ajunge în pagină ca HTML: HTML-ul brut e eliminat, linkurile merg doar la http, https și mailto, cu `rel="noopener noreferrer"` și `target="_blank"`; niciodată `innerHTML` sau `bypassSecurityTrust*` (`check-tasks.sh` 7 le refuză). Sintaxa: paragrafe cu rânduri noi, titluri `#`–`###`, bold, italic, cod inline și blocuri, liste, citate, linii, linkuri și adrese scrise ca atare. Fără `marked` sau `DOMPurify`. Verificat: `check-tasks.sh` 7 (grupul 19b, 2026-11-09).
- [x] Health endpoint `/health` fără date sensibile; Swagger doar în Development. Verificat: `check-backend.sh` (`/health` 200 în Production, Swagger 404 acolo); `/health` răspunde doar `Healthy`, ca text, verificat de mână (grupul 19b, 2026-11-09).
- [x] Backup zilnic `pg_dump` (cron pe server) + volumul de atașamente. Verificat: `check-release.sh` 1 (`backup.sh` și `restore.sh` pe un stack de probă); cron-ul de pe server e al lui Florin, după `docs/backup.md` (grupul 19b, 2026-11-09).

---

## 17. Decizii (ADR) de adăugat în vault

Pe lângă cele două existente (`RBAC pe două niveluri`, `Un singur repo`), decizia de acum produce câte o notă în `Decisions/`:

1. Clean Architecture + CQRS light fără MediatR
2. Utilizatori proprii + JWT cu refresh rotit (nu Identity, nu AD)
3. Angular Signals + servicii, fără NgRx; Angular Material
4. Serilog + Seq pentru loguri; istoricul de task-uri e în DB, nu în loguri
5. Postgres real în teste (Testcontainers), nu InMemory
6. Husky + lint-staged; suita completă la commit/merge pe `main`, `--no-ff` obligatoriu (de la 2026-11-13, partea despre suita pe `main` e înlocuită de decizia „Suita întreagă și gate-ul în GitHub Actions”, `docs/adr/0020`: suita și gate-ul rulează pe GitHub)
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

## 19. Chat cu RAG — pasul 1: `pgvector` și vectorizarea

Chat-ul răspunde la întrebări despre proiecte și task-uri din datele aplicației: caută task-urile după înțeles, iar un model de chat scrie răspunsul din ele. Se construiește pe pași. Pasul 1 e temelia: vectorii în bază, vectorizarea task-urilor și o căutare de probă. Chat-ul propriu-zis vine din pasul 2 (§20). Deciziile: „Vectori în PostgreSQL prin pgvector” (`docs/adr/0021`) și „Modele AI în spatele unei interfețe” (`docs/adr/0022`).

Regulile de aici sunt noi. Secțiunile existente pe care le ating (§2 pachetele, §3 structura, §4.4 permisiunile, §13.3 compose, §15 secretele) se schimbă în același commit cu codul lor, nu înainte.

### 19.1 Baza de date

- **Imagine proprie.** `postgres:17-alpine` nu are `pgvector`. Baza rulează pe o imagine construită din `database/Dockerfile`: o bază fixată la versiune (`postgres:17.11-alpine3.24`, nu eticheta `17-alpine`, care se mută) și `pgvector` la o versiune fixată, construit din sursă, fără `-march=native`.
- **Aceeași imagine peste tot:** compose, teste (Testcontainers o construiește din același Dockerfile), server. Pe server o construiește `release.sh`, nu `compose up` (§13.5). Scripturile `check-*.sh` din CI folosesc PostgreSQL-ul instalat pe mașină, cu pachetul `pgvector` al distribuției (`postgresql-17-pgvector`).
- **Ordinea românească rămâne.** Imaginea păstrează sistemul de bază (Alpine), ca `ro-x-icu` să nu se schimbe sub o bază existentă. Testele de colație trec neschimbate pe ea.
- **Extensia o activează o migrare**, `AddVectorExtension`, cu `DROP EXTENSION` la întoarcere: Npgsql scoate doar adnotarea. EF folosește tipurile ei prin `UseVector()`, care îi dă contextului un `NpgsqlDataSource` propriu, cu pool propriu, pe care `NpgsqlConnection.ClearAllPools()` nu-l golește. Restaurarea are nevoie de extensie în baza în care restaurează.
- **Fără index pe vectori** până în pasul 6. Căutarea filtrează întâi pe coloane, cu indecșii obișnuiți, apoi calculează distanța doar pe rândurile rămase. Rezultatul e exact; un index HNSW ar fi aproximativ și ar filtra după ce caută vecinii.

### 19.2 Tabela vectorilor

- `task_embeddings`: un rând pe task și sursă, cu textul vectorizat, vectorul, numele modelului care l-a făcut și momentul. Rândul se șterge odată cu task-ul.
- În pasul 1 singura sursă e task-ul: titlul și descrierea, ca un singur text. Comentariile vin în pasul 6, atașamentele rămân afară.
- Coloana vectorului e `vector` fără lungime fixă: lungimea e a modelului, iar fiecare rând își ține modelul.
- Filtrele (proiect, asignați, perioadă, arhivat) nu se copiază în tabelă. Se iau prin `task_id` din tabelele existente.
- Task-urile arhivate se vectorizează și ele, pentru că pot intra în căutare la cerere.
- Domain nu depinde de pachetul `Pgvector`. În Domain vectorul e o listă de `float`, iar conversia o face configurația EF. Un test de arhitectură ține regula.

### 19.3 Modelul de vectorizare

- **O interfață în Application**, `IEmbeddingModel`: primește o listă de texte și întoarce vectorii, în aceeași ordine, și tokenii consumați. Implementările stau în Infrastructure: Azure acum, un model local în pasul 6. Application nu referă niciun pachet Azure sau OpenAI; un test de arhitectură ține regula.
- **Niciun test nu cheamă un model real.** CI-ul nu are secrete (§14.8). Testele folosesc un model fals, care dă același vector pentru același text, și vectori dați de test când au nevoie de texte apropiate ca sens. Clientul Azure se testează pe un răspuns fals, fără rețea. O probă pe modelul real, `AzureEmbeddingModelProbe`, se rulează de mână, cu `KANABOL_AZURE_PROBE=1` și secretele `Embeddings:Azure` din user-secrets; altfel e sărită, deci în CI e mereu sărită.
- **Modelul și deployment-ul sunt setări separate.** Apelul la Azure numește deployment-ul, iar lângă vector se scrie numele modelului, ca o redenumire a deployment-ului să nu refacă vectorii.

### 19.4 Configurare și secrete

- Vectorizarea e **oprită implicit** (`Embeddings:Enabled = false`). Oprită, aplicația pornește fără nicio cheie și nu cheamă niciun model.
- Pornită, setările ei se validează la pornire, ca restul opțiunilor (§15): `Embeddings:Model` (numele modelului, fără valoare implicită), `Embeddings:Interval` (între un minut și o zi, 5 minute implicit), `Embeddings:BatchSize` (1–2048, 64 implicit) și `Embeddings:Azure:Endpoint` (o adresă https; http doar pe mașina însăși, `localhost`, `127.0.0.1` sau `::1`, unde cheia nu iese de pe ea, ca pentru înlocuitorul lui Azure din `check-search.sh`), `Embeddings:Azure:Deployment`, `Embeddings:Azure:ApiKey`. Oprită, niciuna nu se verifică. Mesajele de validare numesc setarea, niciodată valoarea. În compose vin din `.env` (`EMBEDDINGS_ENABLED` și celelalte patru, comentate în `.env.example`).
- Cheia stă în `dotnet user-secrets` în dev și în `.env` în Docker. Niciodată în git, în loguri sau în răspunsuri.

### 19.5 Vectorizarea

- Un command, `EmbedTasksCommand`, ia task-urile fără vector, cele cu textul schimbat și cele vectorizate cu alt model decât cel din configurație. Ce trebuie refăcut se află în SQL, comparând textul ținut cu textul compus acum de aceeași expresie care îl trimite la model (`TaskText`: titlul, un rând gol și descrierea). Comparația ia colația titlului, `ro-x-icu`, care e deterministă, deci compară octeții: și o literă mare sau o diacritică schimbată refac vectorul.
- Lucrează în loturi ordonate după id-ul task-ului, cu un apel la model și o salvare pe lot. Un lot căzut nu strică loturile salvate înainte, iar command-ul se oprește la el cu eroarea `Embeddings.ModelFailed`. Oprită vectorizarea, command-ul întoarce `Embeddings.TurnedOff` fără să atingă baza.
- Un text mai lung decât limita modelului nu se trimite așa cum e, ca să nu cadă tot lotul: se trimite doar începutul lui, până la limită, iar logul numește task-ul. Limita și numărarea sunt ale modelului (`IEmbeddingModel.Fit`): pentru modelele OpenAI, 8191 de tokeni, numărați cu tokenizer-ul lor, `cl100k_base`. În tabelă rămâne textul întreg, ca task-ul să nu fie retrimis la fiecare rulare.
- Un worker, `EmbeddingsWorker`, cheamă command-ul după modelul `OverdueTasksWorker`: pornit după migrații, la pornire și apoi la fiecare `Embeddings:Interval`, în loturi de `Embeddings:BatchSize`, într-un scope propriu. O rulare căzută se loghează, iar următoarea reia de la primul task rămas. Cu vectorizarea oprită nu face nimic, deci e oprit implicit. Dacă API-ul se oprește în mijlocul unei rulări, rularea se anulează fără să fie logată ca o eroare, iar loturile salvate rămân.
- Fiecare apel la care modelul a răspuns scrie câți tokeni a consumat, pentru calculatorul de costuri din pasul 4. Scrie un rând în `embedding_calls`, cu scopul (`Vectorization`; întrebarea căutării vine cu §19.6), numele modelului, câte texte au plecat, tokenii și momentul. Rândul nu ține niciun text. Se salvează imediat după răspuns, înaintea vectorilor, ca apelul să fie numărat și când vectorii lui nu se pot salva. Un apel căzut nu are rând, pentru că n-a întors tokeni.
- **Logurile nu țin textele trimise la model** și nici vectorii. Țin cifre: câte texte, câte loturi, câți tokeni, cât a durat.

### 19.6 Căutarea de probă

- Un endpoint primește un text și filtre opționale (proiect, om, perioadă, „și arhivatele”) și întoarce task-urile cele mai apropiate, cu distanța. Întrebarea se vectorizează cu același model ca task-urile.
- **`POST /api/search/tasks`**, nu `GET`, cu întrebarea în body: nginx ține în access log adresa întreagă, iar logurile nu țin niciun text trimis la model (§19.5). Și fiecare căutare scrie apelul ei la model, deci nu e doar o citire.
- **Body-ul:** `question` (1–1000 de caractere, fără caractere ascunse), `projectId`, `assigneeId`, `from` și `to` (data creării task-ului în `[from, to)`; date amândouă, `to` vine după `from`), `includeArchived` și `limit` (1–50, implicit 10). Un `projectId` care nu există dă 404 `Project.NotFound`. `assigneeId` poate fi oricine, ca în rapoarte.
- **Răspunsul:** `tasks`, cele mai apropiate întâi, fiecare cu `id`, `key`, `title`, `projectId`, `projectKey`, `projectName`, `archived` și `distance`, plus `tokens`, câți tokeni a consumat întrebarea.
- **„Și arhivatele”** aduce task-urile arhivate și pe cele din proiectele arhivate. Fără ea, rămân pe dinafară amândouă. O are oricine are `chat.use`, fără `tasks.archive` (decis 2026-11-25).
- În căutare intră doar vectorii modelului din configurație. Vectorii a două modele nu se compară. Un task fără vector încă, de exemplu unul creat de curând, apare după următoarea rulare a worker-ului.
- Distanța e cea cosinus (`<=>`): 0 pentru același sens, mai mare cu cât textele se depărtează.
- **O singură interogare.** Filtrele sunt LINQ în Application, iar `ITaskSearch` din Infrastructure, singurul strat care știe de pgvector, le pune în același SQL: `task_id IN (SELECT …)` și `ORDER BY embedding <=> @vector, task_id`. Întrebarea pleacă drept `Vector`, dintr-o variabilă locală: ca `float[]` ar pleca drept `real[]`, pe care `<=>` nu-l primește.
- **Apelul întrebării** se scrie în `embedding_calls`, cu scopul `Search`, înainte de căutare, ca să fie numărat orice ar urma.
- **Erorile:** cu vectorizarea oprită, 409 `Search.TurnedOff`. Dacă modelul nu răspunde, 503 `Search.ModelFailed`; atunci nu se caută nimic și nu se scrie niciun apel.
- **Permisiunea `chat.use`** cere căutarea și, din pasul 2, chat-ul. Dă singură citire peste toate proiectele, fără `projects.view_all`, deci se dă doar cui are voie să vadă tot. Regula „nimeni nu dă mai mult decât are” (§4.4) se aplică și ei. Se citește din token, ca `reports.view`.
- **Datele de probă:** `scripts/search-data.sh` rulează întâi `scripts/demo-data.sh`, apoi adaugă prin API proiectele a cinci echipe ale unei firme și site-ul ei vechi, arhivat, cu cele 262 de task-uri din `scripts/search-data.txt`. Sunt scrise cum scriu oamenii: unele doar cu titlu, unele fără diacritice, iar aceleași idei revin în alte proiecte, cu alte cuvinte. Scriptul nu cheamă modelul, vectorii îi face worker-ul. Toate task-urile primesc data creării de la rulare, deci filtrul pe perioadă nu se poate judeca pe ele.
- **Verificările pasului:** `scripts/check-search.sh` (§14.7). Pe API-ul real, clientul Azure vorbește prin HTTP cu un înlocuitor al lui Azure, în Node, deci nicio verificare și nicio rulare din CI nu cheamă Azure. Proba pe Azure-ul real rămâne de mână, cu `KANABOL_AZURE_PROBE=1`.

### 19.7 Pentru pașii următori

- **Textul scris de oameni e date, nu instrucțiuni.** Ce scrie cineva într-un task sau într-un comentariu nu schimbă ce face modelul de chat.
- **Cifrele nu vin din căutare.** Pentru totaluri, modelul de chat primește unelte peste rapoartele existente, niciodată SQL scris de el.

---

## 20. Chat cu RAG — pasul 2: primul răspuns

Pasul 2 pune un model de chat peste căutarea din pasul 1: o întrebare primește un răspuns scris din task-urile găsite, cu sursele lui. E doar API, chemat din Swagger. Uneltele pentru cifre vin în pasul 3, conversațiile și costurile în pasul 4, iar pagina în pasul 5. Decizia: „Modele AI în spatele unei interfețe” (`docs/adr/0022`).

Regulile de aici sunt noi și sunt scrise înaintea codului. Secțiunile existente pe care le ating (§2 pachetele, §3 structura, §4.6 erorile, §5.1 entitățile, §13.3 compose, §15 secretele) se schimbă în același commit cu codul lor, ca la §19. Ce rămâne de ales la implementare e numit în subsecțiunea lui și intră aici odată cu codul care îl alege.

### 20.1 O întrebare, un răspuns

- Fiecare întrebare e de sine stătătoare. Cererea nu primește nimic din întrebările dinainte, și nicio conversație nu se salvează până în pasul 4.
- Răspunsul e scris doar din ce întorc uneltele. În pasul 2 e o singură unealtă, căutarea task-urilor după înțeles.
- Cere `chat.use`, din token, ca și căutarea (§19.6).
- Întrebarea vine în body-ul unui `POST`, din același motiv ca la căutare: adresa ajunge în access log, iar textele trimise la un model nu ajung în niciun log. Numele rutei se alege la implementare.

### 20.2 Bucla de unelte

- Aplicația trimite modelului instrucțiunile ei, întrebarea omului și descrierea uneltelor. Modelul întoarce textul final sau o cerere de unealtă, cu parametrii completați de el. Aplicația execută unealta, îi trimite rezultatul și cheamă modelul din nou, până la un răspuns sau până la limita de runde.
- **Modelul nu atinge baza.** El cere, iar aplicația execută, în codul ei. Nu primește SQL, nu primește o conexiune și nu hotărăște ce are voie să vadă. Permisiunile, filtrele și limitele rămân ale aplicației.
- **Limite pe fiecare cerere:** câte runde de unelte, câți tokeni are răspunsul și cât e așteptat modelul. Sunt setări (§20.6), cu valorile alese la implementare. Tot atunci se alege ce primește omul când se atinge limita de runde.
- Bucla stă în Application, într-un command, și nu știe ce furnizor e în spatele interfeței.

### 20.3 Unealta de căutare

- **Aceeași căutare** ca la `POST /api/search/tasks` (§19.6): aceeași interogare și aceleași reguli pentru vectori și arhivate. Nu e o a doua implementare.
- **Textul căutării îl scrie modelul**, din întrebarea omului, și poate căuta de mai multe ori pentru o întrebare. Proba pe modelul real a arătat că o întrebare vagă ratează task-ul, iar una care numește lucrul îl găsește.
- **Parametrii modelului:** textul, proiectul (cheie sau nume), omul (nume), perioada și câte rezultate. Modelul nu primește și nu trimite id-uri.
- **Proiectul și omul se caută în bază.** O valoare negăsită se întoarce modelului ca negăsită, cu numele apropiate, niciodată sărită în tăcere: o căutare fără filtrul cerut ar răspunde la altă întrebare.
- **Perioada:** modelul alege, după întrebare, între data creării și data finalizării task-ului, iar răspunsul spune ce dată a folosit (decis 2026-11-26). Endpoint-ul de căutare primește aceeași alegere odată cu codul ei.
- **„Și arhivatele” nu e parametru al modelului.** O alege omul, în cerere, iar unealta o aplică la fiecare căutare a cererii.
- **Ce întoarce:** datele task-ului, nu doar titlul: cheia, titlul, descrierea, proiectul, coloana, asignații, când a fost creat și terminat, dacă e arhivat.
- **Distanța nu e prag.** Nicio căutare nu taie rezultatele după distanță: în probă, cea mai mică distanță a fost a unui rezultat greșit. Modelul primește rezultatele și judecă el.
- De ales la implementare: cât din descriere pleacă la model, câte rezultate primește implicit și dacă primește și distanța.

### 20.4 Răspunsul

- **Textul**, în română.
- **Sursele:** doar task-urile pe care răspunsul le numește și pe care o căutare a cererii chiar le-a întors (decis 2026-11-26). Le stabilește aplicația, din rezultatele uneltelor, nu modelul: o cheie pe care modelul o scrie fără s-o fi primit nu ajunge sursă.
- **Filtrele folosite:** proiectul, omul, perioada cu felul datei, arhivatele.
- **Tokenii:** ai căutărilor și ai modelului de chat, la intrare și la ieșire.

### 20.5 Ce nu face

- **Nimic găsit:** spune limpede că n-a găsit nimic în task-uri și ce a căutat. Nu răspunde din cunoștințele generale ale modelului (decis 2026-11-26).
- **Cifre:** până la uneltele din pasul 3, spune că nu poate număra încă și trimite la pagina Rapoarte (decis 2026-11-26). Căutarea aduce câteva task-uri, nu toate, deci o numărătoare din ele ar fi greșită (§19.7).
- **Textul scris de oameni e date** (§19.7). Ce întorc uneltele ajunge la model ca rezultat de unealtă, despărțit de instrucțiunile aplicației, niciodată lipit în ele, iar instrucțiunile spun că ce scrie într-un task nu se execută. Testele țin locul în care ajunge textul. Cum se poartă un model real cu un task care îi cere altceva se vede doar în proba de mână.

### 20.6 Modelul de chat

- **O interfață în Application**, `IChatModel`, lângă `IEmbeddingModel`: primește mesajele și descrierea uneltelor și întoarce textul sau cererile de unealtă, plus tokenii, cei de intrare separat de cei de ieșire. Implementarea stă în Infrastructure. Application nu referă pachetul niciunui furnizor; testul de arhitectură din §19.3 îl acoperă și pe acesta.
- **Furnizorul** se alege înainte de implementare, după o comparație de mână: prețul la intrare și la ieșire, chemarea de unelte, data de retragere a modelului și româna. Interfața nu depinde de alegere.
- **Niciun test nu cheamă un model real** (§19.3). Testele folosesc un model fals, care răspunde după scenariul dat de test: cere o unealtă, apoi scrie un răspuns. Clientul furnizorului se testează pe răspunsuri false, fără rețea. Proba pe modelul real se rulează de mână, cu o variabilă de mediu; altfel e sărită, deci în CI e mereu sărită.
- **Oprit implicit** (`Chat:Enabled = false`). Oprit, aplicația pornește fără nicio cheie de chat și nu cheamă niciun model de chat. Pornit, cere și vectorizarea pornită (§19.4), pentru că fără ea n-are ce căuta. Setările lui se validează la pornire doar când e pornit, iar mesajele de validare numesc setarea, niciodată valoarea.
- Cheia stă unde stă și cea de vectorizare (§19.4): `dotnet user-secrets` în dev și `.env` în Docker. Niciodată în git, în loguri sau în răspunsuri.
- **Erorile** urmează căutarea (§19.6): cu chat-ul oprit, 409 `Chat.TurnedOff`; dacă modelul nu răspunde, 503 `Chat.ModelFailed`.

### 20.7 Tokenii și logurile

- Fiecare apel la care modelul de chat a răspuns își scrie tokenii, cei de intrare separat de cei de ieșire, pentru că au prețuri diferite. Rândul se scrie imediat după răspuns, ca la vectorizare (§19.5), și nu ține niciun text. Căutările cererii își scriu apelurile ca până acum, cu scopul `Search`. Unde se scriu apelurile de chat, într-o tabelă nouă sau în `embedding_calls` lărgită, se alege la implementare.
- La fiecare rundă modelul primește din nou tot ce s-a spus până atunci: instrucțiunile, întrebarea, cererile lui și rezultatele. Tokenii de intrare cresc deci cu fiecare rundă, iar limita de runde e și o limită de cost.
- **Logurile nu țin** întrebarea, răspunsul, cererile de unealtă sau rezultatele lor (§19.5). Țin cifre: câte runde, câte căutări, câți tokeni, cât a durat.

### 20.8 Verificările pasului

- `scripts/check-chat.sh` (§14.7), pe API-ul real, cu un înlocuitor al modelului de chat, în Node, care cere unealta și apoi răspunde, ca înlocuitorul lui Azure din `check-search.sh`. Nicio verificare și nicio rulare din CI nu cheamă un model real.
- **Pasul e gata când**, din Swagger: o întrebare despre task-uri primește un răspuns cu surse care există; una fără nimic potrivit primește „n-am găsit”; una cu cifre primește trimiterea la Rapoarte; un task cu instrucțiuni în descriere nu schimbă ce face chat-ul. Testele trec fără nicio cheie.
