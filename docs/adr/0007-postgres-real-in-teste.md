# 0007. Postgres real în teste

## Decizie
Testele de handler și de endpoint rulează pe PostgreSQL real, pornit cu Testcontainers, iar Respawn resetează tabelele între teste.

## Motiv
- `xmin`, `timestamptz`, `snake_case` și comportamentul tranzacțiilor diferă de la un provider la altul.
- Testele pe InMemory dau încredere falsă.
- Costul e acceptabil: 5-10 secunde pentru pornirea containerului și Docker pornit local.

## Alternative
EF InMemory sau SQLite: mai rapide, dar nu se comportă ca baza de date din producție.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Husky și lint-staged la commit|Husky și lint-staged la commit]]
