# 0011. Fără MediatR, AutoMapper, Moq și Karma

## Decizie
Nu folosim MediatR, AutoMapper, Moq și Karma.

## Motiv
- MediatR și AutoMapper au trecut pe licență comercială în 2025.
- Moq a avut incidentul SponsorLink.
- Karma a fost abandonat de Angular.

## Alternative
Ce folosim în loc:
- MediatR: handlere injectate direct și decoratori prin Scrutor.
- AutoMapper: mapping manual, cu constructorul DTO-ului sau o metodă statică `FromEntity`.
- Moq: NSubstitute.
- Karma: Vitest, prin builder-ul Angular.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Clean Architecture și CQRS light|Clean Architecture și CQRS light]]
