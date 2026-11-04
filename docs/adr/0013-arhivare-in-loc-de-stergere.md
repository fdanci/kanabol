# 0013. Arhivare în loc de ștergere

## Decizie
Task-urile și proiectele nu se șterg, ci se arhivează (`archived_at`, `archived_by_id`) și pot fi restaurate. `DELETE /api/tasks/{id}` arhivează, iar API-ul nu are hard delete.

## Motiv
- Comentariile, atașamentele și istoricul rămân intacte și intră în rapoarte.
- Un global query filter ascunde implicit ce e arhivat.
- Cine are permisiunea `tasks.archive` vede task-urile arhivate și le poate restaura.

## Alternative
- Hard delete în API: s-ar pierde istoricul. Curățarea fizică, dacă va fi vreodată nevoie, e o operațiune de DBA.
- Soft delete generic pe toate entitățile: comentariile și istoricul nu se arhivează separat, ele urmează task-ul.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
