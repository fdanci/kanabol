# 0003. Clean Architecture și CQRS light

## Decizie
Backendul e împărțit în patru proiecte (`Domain`, `Application`, `Infrastructure`, `Api`), cu dependențe doar spre interior. Fiecare caz de utilizare e un command sau un query cu handler propriu, injectat direct în controller, fără dispatcher.

## Motiv
- Lucrez singur și am timp limitat, așa că prefer convenții clare și puțin ceremonial.
- Regulile de business rămân în `Domain` și `Application`, separate de EF, HTTP și DI.
- Un handler înseamnă o tranzacție și un `SaveChangesAsync`, iar `DbContext` e deja Unit of Work.
- Validarea și logarea se adaugă cu decoratori prin Scrutor, fără pipeline de MediatR.

## Alternative
- MediatR pentru dispatch și pipeline: a trecut pe licență comercială în 2025.
- Repository generic peste EF: o abstracție inutilă peste ce oferă deja `DbContext`.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Fără MediatR, AutoMapper, Moq și Karma|Fără MediatR, AutoMapper, Moq și Karma]]
