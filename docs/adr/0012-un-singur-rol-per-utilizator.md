# 0012. Un singur rol per utilizator

## Decizie
Un utilizator are exact un rol la nivel de aplicație (`users.role_id`, NOT NULL).

## Motiv
- Dacă cineva are nevoie de drepturi combinate, adminul creează un rol nou, fără schimbări în cod.
- Permisiunile unui utilizator sunt exact cele ale rolului lui.
- Schimbarea rolului invalidează refresh token-urile, deci permisiunile din JWT se reîmprospătează la următorul login sau refresh.

## Alternative
Mai multe roluri per utilizator (many-to-many): drepturile combinate se obțin deja cu un rol nou.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/RBAC pe două niveluri|RBAC pe două niveluri]]
