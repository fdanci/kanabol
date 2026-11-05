# SPEC — Kanabol (Kanban pe steroizi)

> Specificație tehnică pentru prima versiune. Produsul și regulile de business sunt în vault-ul Obsidian (`_Overview.md`, `_Architecture.md`, `Decisions/`). Acest fișier stabilește **cum** construim: arhitectură, pattern-uri, convenții, loguri, teste, lint, Docker și pipeline-ul local de commit.
>
> Stare: v1.7 — 2026-11-05 (v1.6 + deciziile grupului 8: cine poate da ce și contul dezactivat în §4.4, invarianta ultimului `Admin` la concurență în §4.4, §5.1 și §5.3, `ux_roles_lower_name` în §5.1 și §7.2, `check-users.sh` în §14.7).

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

Pachete NuGet cheie: `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `FluentValidation`, `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Swashbuckle.AspNetCore`, `Scrutor` (decoratori), `Testcontainers.PostgreSql`, `Respawn`, `xunit`, `NSubstitute`, `FluentAssertions`.

Pachete npm cheie: `@angular/*`, `@angular/material`, `@angular/cdk`, `@angular/localize`, `@microsoft/signalr`, `chart.js` (fără wrapper Angular: un component `kb-chart` peste `<canvas>`, pentru rapoarte), `angular-eslint`, `typescript-eslint`, `prettier`, `vitest`, `husky`, `lint-staged`, `@commitlint/cli`, `@commitlint/config-conventional`.

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
| **Infrastructure** | `AppDbContext`, configurări EF, migrări, `FileStorage` pe disc, JWT, hashing parole, `SystemClock`, background jobs (alerte), SignalR notifier | Logică de business |
| **Api** | Controllers, filtre, middleware (correlation id, exception handler), `Program.cs`, autorizare pe permisiuni, SignalR hubs | Logică de business, acces direct la DbContext |

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

        var access = await projectAccess.EnsureMemberAsync(task.ProjectId, currentUser.Id, ct);
        if (access.IsFailure) return access.Error;

        task.MoveTo(cmd.TargetColumnId, cmd.Position, currentUser.Id, clock.UtcNow); // ridică TaskMovedEvent
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

Reguli:
- Un handler = o tranzacție = un `SaveChangesAsync`. `DbContext` este Unit of Work; **nu** facem Repository generic peste EF (abstracție inutilă). `IAppDbContext` expune doar `DbSet`-urile și `SaveChangesAsync`.
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
| **Background service** | `OverdueTasksWorker : BackgroundService` cu `PeriodicTimer` (la 15 min): task-uri cu `due_date < now`, neterminate, nearhivate, fără notificare `TaskOverdue` deja emisă → creează `Notification` pentru asignat și pentru owner-ii proiectului → `NotificationCreatedEvent` → SignalR. O singură notificare per task, nu se repetă zilnic | Alerte deadline in-app, fără scheduler extern |
| **Domain event handlers** pentru notificări | `TaskAssignedEvent` → `Notification(TaskAssigned)` pentru noul asignat, dacă nu e el cel care a făcut asignarea; `CommentAddedEvent` → `Notification(CommentAdded)` pentru asignat și pentru creatorul task-ului, fără autorul comentariului; ambele → `NotificationCreatedEvent` → SignalR `user:{id}` | Clopoțelul e util din prima zi, nu doar la întârzieri |

### 4.4 Autorizare

**Nivel aplicație — permisiuni.** Codul verifică permisiuni, niciodată nume de roluri. Lista inițială (constante în `Kanabol.Domain.Authorization.Permissions`):

```
users.manage        roles.manage        projects.create
projects.view_all   projects.manage     tasks.create
tasks.archive       reports.view
```

`tasks.create` se verifică pe lângă apartenența la proiect: creezi task-uri doar în proiectele în care ești membru și doar dacă rolul tău are permisiunea (UI §7.4). Apartenența e cea reală, un rând în `project_members`, nu cea derivată din `projects.view_all` sau `projects.manage`: cine administrează un proiect străin se adaugă întâi ca membru. Un rol poate deci vedea un proiect fără să poată adăuga task-uri în el.

**Un utilizator are exact un rol** (`users.role_id`, NOT NULL). Rolul grupează permisiuni (`role_permissions`). Dacă cineva are nevoie de drepturi combinate, adminul creează un rol nou — nu atribuim mai multe roluri aceleiași persoane. Schimbarea rolului invalidează refresh token-urile utilizatorului (permisiunile din JWT se reîmprospătează la următorul login/refresh).

Permisiunile ajung în JWT ca claims `permission`. Pe API: `[HasPermission(Permissions.ProjectsViewAll)]` — un `IAuthorizationPolicyProvider` custom construiește politica `Permission:<name>` la cerere, fără să le înregistrăm pe fiecare în `Program.cs`.

**Cine poate da ce.** Nimeni nu dă mai mult decât are. Un rol se creează, se editează sau se șterge doar de cineva care are toate permisiunile lui, cele vechi și cele noi (403 `Role.PermissionsBeyondYours`). Un utilizator primește, la creare sau la schimbarea rolului, doar un rol ale cărui permisiuni le are apelantul (403 `User.RoleBeyondYours`), iar nimeni nu editează, nu mută în alt rol și nu dezactivează pe cineva care are permisiuni în plus față de el (403 `User.PermissionsBeyondYours`). Altfel `users.manage` singur ar duce la `Admin`, iar `roles.manage` ar putea goli rolul cuiva aflat mai sus, ca apoi să-l dezactiveze. `GET /api/users/assignable-roles` întoarce rolurile pe care apelantul le poate da. Adminul are toate permisiunile, deci limita nu-l atinge.

**Cont dezactivat sau retrogradat.** Administrarea utilizatorilor și a rolurilor nu se încrede în token: `CurrentPermissions` recitește din baza de date utilizatorul și permisiunile lui la fiecare cerere (401 `Auth.SessionEnded` dacă e dezactivat, 403 `Auth.PermissionRequired` dacă permisiunea a dispărut), iar dezactivarea închide sesiunile (refresh token-urile sunt revocate). Celelalte endpoint-uri se bazează pe claims din access token, deci un cont dezactivat sau retrogradat le mai poate apela până expiră token-ul, cel mult 15 minute (§4.5). Acceptat așa în v1, pe 2026-11-05.

**Nivel proiect — rol pe proiect.** `ProjectMember(ProjectId, UserId, ProjectRole)` cu `ProjectRole = Owner | Member`. Verificat în handler prin `IProjectAccess`:
- `EnsureMemberAsync(projectId, userId)` — trece dacă e membru **sau** are `projects.view_all` **sau** `projects.manage` (cine poate administra orice proiect îl poate și vedea).
- `EnsureOwnerAsync(projectId, userId)` — trece dacă e owner **sau** are `projects.manage`.

Refuzul e un 403 ProblemDetails: `code: Auth.PermissionRequired` pentru o permisiune lipsă (numele permisiunii apare doar în log, la `Warning`, nu în răspuns), `Project.MemberRequired` / `Project.OwnerRequired` pentru rolul pe proiect. `IProjectAccess` răspunde din baza de date, despre `userId`-ul primit, nu din claims: refuză utilizatorii dezactivați, nu verifică dacă proiectul există (handlerul răspunde 404 înainte) și păstrează owner-ii unui proiect arhivat.

Invariante:
- `Project` are mereu cel puțin un owner (verificat în `Project.RemoveMember` / `ChangeMemberRole`).
- Rolul `Admin` (`is_system = true`) nu poate fi șters, redenumit sau lipsit de permisiuni.
- Ultimul utilizator cu rolul `Admin` nu poate primi alt rol și nu poate fi dezactivat (verificat în handlerele `ChangeUserRole` și `DeactivateUser`, prin `LastAdminGuard`). O numărare nu ajunge când cererile se suprapun: `Role.HoldersVersion` crește de câte ori cineva pleacă din rol și e concurrency token, la fel `users.role_id`, deci a doua din două salvări simultane, sau una făcută pe o citire veche, primește 409 `User.ChangedMeanwhile`.
- Utilizatorii nu se șterg, se dezactivează (`is_active = false`): rămân referențiați din istoric, comentarii și rapoarte.

### 4.5 Autentificare

**Token-uri**

- `POST /api/auth/login` → access token JWT (15 min, în body) + refresh token (7 zile) în cookie `refresh_token`: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
- Access token-ul are claims `sub`, `permission` (câte unul pe permisiune) și `must_change_password`, doar când e cazul. E semnat HS256 și se validează fără toleranță de ceas.
- `POST /api/auth/refresh` rotește refresh token-ul (cel vechi devine invalid; refolosirea unuia invalidat revocă toată familia — detecție de furt). Două cereri simultane cu același token contează tot ca refolosire: `revoked_at` e concurrency token, deci a doua salvare nu mai găsește rândul.
- `POST /api/auth/logout` revocă familia sesiunii curente și șterge cookie-ul. Răspunde 204 și fără cookie.
- Login, refresh și logout sunt anonime, pentru că sunt tot ce-i rămâne unui client cu access token-ul expirat. Cookie-ul dovedește cine cheamă, iar `SameSite=Strict` îl ține departe de alte site-uri.
- Refresh token-urile se stochează hash-uite (SHA-256) în `refresh_tokens`.
- Frontend ține access token-ul **doar în memorie** (semnal în `AuthStore`), niciodată în `localStorage`.
- SignalR primește token-ul prin query string `access_token` la handshake (standard).

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

- `User`: `Email` (unic, păstrat cu litere mici), `DisplayName`, `PasswordHash`, `RoleId` (exact un rol; concurrency token, §4.4), `IsActive`, `FailedLoginAttempts`, `LockedUntil?`, `MustChangePassword`.
- `Role`: `Name` (unic indiferent de majuscule, `ux_roles_lower_name`), `IsSystem` (true doar pentru `Admin`), `HoldersVersion` (crește când cineva pleacă din rol; concurrency token care ține invarianta ultimului `Admin`, §4.4), permisiuni prin `role_permissions`.
- `Project`: `Name`, `Key` (prefixul cheilor de task: 2–5 majuscule, unic, ex. `KAN`; nu se schimbă după creare), `Description?`, `NextTaskNumber` (contorul din care se alocă numerele de task), `ArchivedAt?`, `ArchivedById?`; deține `Members` și `Columns`.
- `TaskItem` (nu `Task`, ca să nu se bată cu `System.Threading.Tasks.Task`): `Number` (unic în proiect, alocat crescător la creare din `Project.NextTaskNumber`, în aceeași tranzacție; `Project` are concurrency token, deci două creări simultane nu primesc același număr), `Title`, `Description` (markdown de bază, stocat ca text), `Priority` (enum `Low|Medium|High|Critical`), `DueDate?`, `AssigneeId?`, `ColumnId`, `Position`, `CompletedAt?`, `ArchivedAt?`, `ArchivedById?`. Cheia afișată e `{Project.Key}-{Number}`, ex. `KAN-123`; `GET /api/tasks/by-key/{key}` o rezolvă (UI §7.5). Numărul rămâne și după arhivare, nu se refolosește.
- `BoardColumn`: `Name`, `Position`, `IsDone` (marchează coloana „terminat”; intrarea unui task într-o astfel de coloană setează `CompletedAt`, ieșirea îl anulează). Fără WIP limit în v1.
- `TaskHistoryEntry`: `Field`, `OldValue`, `NewValue`, `ChangedById`, `ChangedAt`. Entitatea `TaskItem` o adaugă singură în metodele de modificare (`Rename`, `ChangeDescription`, `MoveTo`, `Assign`, `SetPriority`, `SetDueDate`, `Archive`, `Restore`) — istoricul e garantat, nu depinde de handler.
- `Notification`: `UserId`, `Type` (enum `TaskOverdue|TaskAssigned|CommentAdded`), `TaskId?`, `Message`, `CreatedAt`, `ReadAt?`. Cine le produce e în §4.3: worker-ul pentru `TaskOverdue`, handlerele de domain events pentru celelalte două. Livrate exclusiv in-app (listă + clopoțel + push prin SignalR). **Nu** definim `IEmailSender` sau alt canal în v1; dacă apare nevoia, se adaugă atunci ca un consumer suplimentar al acelorași domain events.

**Arhivare, nu ștergere.** `DELETE /api/tasks/{id}` arhivează (`ArchivedAt = now`), nu șterge rânduri. Global query filter `HasQueryFilter(t => t.ArchivedAt == null)` pe `TaskItem`; comentariile, atașamentele și istoricul rămân intacte și intră în rapoarte. Admin (permisiunea `tasks.archive`) vede task-urile arhivate cu `?includeArchived=true` (handler cu `IgnoreQueryFilters()`) și le poate restaura (`POST /api/tasks/{id}/restore`). Nu există hard delete în API; curățarea fizică (dacă va fi vreodată nevoie) e o operațiune de DBA, nu o funcție de produs. Proiectele urmează aceeași regulă (`archived_at` pe `projects`).

### 5.1.1 Productivitate (feature `Reports`)

Nu e entitate; sunt trei queries read-only, filtrabile pe interval (`from`, `to`), pe proiect și pe utilizator. Pagina globală de rapoarte cere `reports.view`. Owner-ul unui proiect le vede fără permisiune, dar doar pentru proiectul lui, din tab-ul „Rapoarte” al paginii de proiect (UI §7.3): query-urile acceptă deci fie `reports.view`, fie owner pe `projectId`-ul din filtru, care devine obligatoriu în al doilea caz.

| Metrică | Definiție | Sursă |
|---|---|---|
| **Task-uri finalizate** | Număr de task-uri cu `completed_at` în interval, grupate pe asignat și pe proiect; serie pe săptămână/lună | `task_items.completed_at`, `assignee_id` |
| **Lead time / cycle time** | Lead: `completed_at − created_at`. Cycle: de la prima ieșire a task-ului din prima coloană a board-ului (poziția 0, de obicei „De făcut”) până la `completed_at`; un task creat direct în altă coloană pornește de la `created_at`. Nu există marcaj de coloană „în lucru”, regula e poziția. Plus timp mediu per coloană. Raportate ca medie și mediană | `task_history_entries` cu `field = 'ColumnId'` (reconstruim intervalele de ședere per coloană) |
| **Deadline** | Task-uri întârziate (`due_date < now` și neterminate), task-uri finalizate după deadline, rata de respectare = finalizate la timp / finalizate cu deadline | `due_date`, `completed_at` |

Toate exclud task-urile arhivate înainte de finalizare. Calculul se face în EF cu proiecții și `GroupBy`; dacă query-ul pe istoric devine lent, se materializează într-o tabelă `task_column_stays` (`task_id, column_id, entered_at, left_at`) întreținută din evenimentul `TaskMovedEvent` — decizie amânată până apar date reale.

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
- Concurență optimistă: `builder.Property<uint>("Version").IsRowVersion()` pe `TaskItem`, `Project`, `BoardColumn` (mutările pe kanban concurente se rezolvă cu 409). Npgsql o mapează pe `xmin`; `UseXminAsConcurrencyToken` nu mai există în Npgsql 10. Separat, `IsConcurrencyToken()` pe `Role.HoldersVersion` și `User.RoleId` (invarianta ultimului `Admin`, §4.4) și pe `RefreshToken.RevokedAt` (rotirea refresh token-ului, §4.5).
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
│   ├── logging/             # Logger, GlobalErrorHandler
│   └── layout/              # shell, navbar, sidenav
├── shared/                  # reutilizabil, fără stare
│   ├── ui/                  # componente prezentaționale generice (confirm-dialog, empty-state, ...)
│   ├── pipes/
│   ├── directives/          # hasPermission
│   └── models/              # tipuri comune (Page<T>, ProblemDetails)
└── features/
    ├── auth/                # login, change-password
    ├── users/
    ├── roles/
    ├── projects/
    ├── board/               # kanban: board-page, column, task-card, cdk drag & drop
    ├── tasks/               # task-detail, comments, attachments, history
    ├── notifications/
    └── reports/
```

Fiecare feature: `<feature>.routes.ts`, `<feature>-api.ts` (HTTP), `<feature>-store.ts` (state cu signals), `models.ts`, componente. Feature-urile nu se importă între ele direct; ce e comun urcă în `shared/`.

### 6.2 Pattern-uri folosite în frontend

| Pattern | Cum |
|---|---|
| **Smart / presentational** | `*-page` (container) injectează store-ul și pasează date prin `input()`/`output()`; componentele de sub nu injectează servicii de date |
| **Store per feature cu Signals** | `@Injectable({ providedIn: 'root' }) class BoardStore` cu `signal` privat + `computed` public + metode (`load`, `moveTask`). Fără NgRx |
| **Resource API pentru citiri** | `resource()` / `httpResource()` pentru încărcări dependente de un parametru (ex. board-ul proiectului curent); stare `loading/error` gratis |
| **Optimistic update** | Mutarea unui card actualizează semnalul imediat, apoi apelează API; la eroare face rollback și arată snackbar |
| **Facade API** | `*-api.ts` este singurul loc cu `HttpClient` pentru feature; întoarce `Observable<Dto>` tipat |
| **Interceptori funcționali** | `authInterceptor` (Bearer + refresh la 401, o singură dată, cu coadă; refresh-ul e serializat și între taburi cu `navigator.locks`, altfel două taburi pornite odată declanșează detecția de refolosire din §4.5), `correlationInterceptor` (`X-Correlation-Id`), `errorInterceptor` (ProblemDetails → mesaj) |
| **Route guards funcționali** | `authGuard`, `permissionGuard('projects.view_all')` |
| **Directivă `*kbHasPermission`** | Ascunde elemente din UI; **nu** e securitate, backend-ul verifică oricum |
| **`@defer`** pentru widget-uri grele (rapoarte, editor de descriere) |
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
| FK | `<entitate>_id` | `project_id`, `assignee_id` |
| Indexuri | `ix_<tabel>_<coloane>` (generat de EF) | `ix_task_items_project_id_column_id` |
| Unique | `ux_<tabel>_<coloane>` | `ux_users_email`, `ux_roles_lower_name` (pe `lower(name)`, creat cu SQL în migrare), `ux_projects_key`, `ux_task_items_project_id_number` |
| FK constraint | `fk_<tabel>_<tabel_referit>_<coloană>` (generat) | |
| Timestamp-uri | `timestamptz`, sufix `_at` | `completed_at` |
| Booleans | prefix `is_`/`has_` | `is_done`, `is_active` |
| Enum-uri | `text` | `priority = 'High'` |
| Join tables | numele ambelor entități | `role_permissions`, `project_members` |
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
- **Ce NU logăm**: parole, token-uri (nici hash-ul), conținutul atașamentelor, body-ul complet al request-urilor. Dacă e nevoie de debugging pe body, se activează explicit și temporar în dev.
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

- `Logger` (`core/logging`): `debug/info/warn/error`; în dev scrie în consolă; în prod `warn`+`error` se trimit la `POST /api/client-logs` (batch, max 10/min, cu `correlationId`, URL, user agent) ca să apară în Seq alături de cele de backend.
- `GlobalErrorHandler` (`ErrorHandler` Angular): prinde erorile neprinse → `Logger.error` + snackbar generic.
- Fără `console.log` în cod comis (regulă ESLint `no-console: ["error", { allow: ["warn", "error"] }]`; Logger-ul are excepție).

---

## 9. Real-time (SignalR)

- Hub `BoardHub` la `/hubs/board`, autorizat cu JWT. Clientul apelează `JoinProject(projectId)` → grup `project:{id}`; serverul verifică accesul prin `IProjectAccess`.
- Evenimente către client: `TaskCreated`, `TaskMoved`, `TaskUpdated`, `TaskArchived`, `TaskRestored`, `CommentAdded` (grup `project:{id}`), `NotificationCreated` (grup `user:{id}` — clientul intră automat în grupul propriu la conectare; alimentează clopoțelul din navbar).
- Sursa: domain events → `IDomainEventHandler<TaskMovedEvent>` în Application → `IBoardNotifier` (interfață în Application, implementată în Api peste `IHubContext<BoardHub>`). Handlerele de domeniu nu știu de SignalR.
- Client: `Realtime` service (`core/realtime`) cu `HubConnectionBuilder`, reconectare automată, re-join la reconectare; store-urile se abonează.

---

## 10. Atașamente

- `IFileStorage { SaveAsync(stream, fileName, ct) → StoredFile(Key, Size, ContentType); OpenReadAsync(key); DeleteAsync(key) }`.
- Implementare v1: `LocalFileStorage` → `/data/attachments/{yyyy}/{MM}/{guid}{ext}` (volum Docker). Metadatele (`FileName`, `ContentType`, `Size`, `StorageKey`, `UploadedById`) în `attachments`.
- Limite: 25 MB/fișier, listă albă de extensii (configurabilă), numele original nu ajunge niciodată pe disc.
- Download prin `GET /api/attachments/{id}` cu verificarea accesului la task; `Content-Disposition: attachment`.

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

nginx are trei fișiere. Ce au comun dev și prod stă în `nginx-app.conf`, inclus din `nginx.conf` (dev) și din `nginx.prod.conf`, pe care `compose.prod.yaml` îl montează peste `nginx.conf`.

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
}
location / { try_files $uri $uri/ /index.html; }
# nginx doesn't know this extension, and would send it as a binary download.
location = /manifest.webmanifest { default_type application/manifest+json; }

gzip on;
gzip_types text/css application/javascript application/json image/svg+xml;
add_header X-Content-Type-Options nosniff;
add_header X-Frame-Options DENY;
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
- Durată estimată pe `main`: build ~15 s + container Postgres ~8 s + teste; țintim < 90 s total. Pe feature branches limita e 40 s: un commit care atinge și backend, și frontend durează 21–22 s, unul pe o singură parte 14–18 s.
- Pe Windows, hook-urile rulează prin `sh` din Git for Windows; scripturile sunt POSIX sh (fără bash-isme) și au `eol=lf` prin `.gitattributes`.

### 14.7 Verificări de acceptanță

Fiecare grup din `Tasks.md` are verificările lui în `scripts/check-*.sh`. Nu rulează în hook-uri: se rulează toate pe o clonă curată, după `scripts/setup.sh`, înainte ca un grup să fie considerat gata, pentru că repo-ul n-are CI.

- `check-tests.sh`, `check-auth.sh`, `check-permissions.sh`, `check-users.sh`, `check-pipeline.sh` și `check-docker.sh` au nevoie de Docker pornit.
- `check-backend.sh`, `check-auth.sh`, `check-permissions.sh` și `check-users.sh` au nevoie de un PostgreSQL al cărui utilizator poate crea baze de date, din `KANABOL_CHECK_DB` sau din user secret-ul `ConnectionStrings:Default`. Lucrează în baze proprii și le șterg la final.
- `check-docker.sh 5` are nevoie de porturile 5432, 5341 și 5200 libere.

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
- [ ] `HSTS`, `X-Content-Type-Options`, `X-Frame-Options`, CSP de bază (`default-src 'self'`).
- [ ] Rate limiting pe login; lockout 15 min după 10 eșecuri consecutive (câmp `locked_until` pe `users`).
- [ ] Parole: minim 10 caractere, verificate contra unei liste scurte de parole comune.
- [ ] Refresh token rotit, revocare la logout și la schimbarea parolei.
- [ ] Validare de input în toate commands (FluentValidation); EF parametrizat → fără SQL injection.
- [ ] Atașamente: extensii permise, dimensiune max, nume generat, `Content-Disposition: attachment`, nu se execută niciodată din `wwwroot`.
- [ ] Backend verifică permisiunea și accesul la proiect la fiecare request; UI doar ascunde.
- [ ] Markdown-ul din descrieri și comentarii se randează în client printr-un parser cu sanitizare: HTML brut eliminat, fără script sau atribute `on*`, linkurile cu `rel="noopener noreferrer"`; niciodată `innerHTML` nefiltrat sau `bypassSecurityTrust*`.
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
- Intervalul exact al `OverdueTasksWorker` (propus 15 min). Că notificarea se emite o singură dată per task e decis în §4.3.
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
