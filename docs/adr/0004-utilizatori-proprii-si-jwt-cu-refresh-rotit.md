# 0004. Utilizatori proprii și JWT cu refresh rotit

## Decizie
Aplicația își gestionează singură utilizatorii. Autentificarea folosește un access token JWT de 15 minute și un refresh token de 7 zile, rotit la fiecare folosire și ținut într-un cookie `HttpOnly`.

## Motiv
- Prima versiune nu are integrări externe.
- Access token-ul stă doar în memorie în frontend, niciodată în `localStorage`.
- Rotirea permite detectarea furtului: refolosirea unui token invalidat revocă toată familia.
- Refresh token-urile se stochează hash-uite (SHA-256), iar parolele cu `PasswordHasher<User>` (PBKDF2).

## Alternative
- ASP.NET Core Identity complet: din el avem nevoie doar de `PasswordHasher<User>`.
- Active Directory: ar fi o integrare externă.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/RBAC pe două niveluri|RBAC pe două niveluri]]
