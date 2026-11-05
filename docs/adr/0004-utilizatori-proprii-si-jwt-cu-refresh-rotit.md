# 0004. Utilizatori proprii și JWT cu refresh rotit

## Decizie
Aplicația își gestionează singură utilizatorii. Autentificarea folosește un access token JWT de 15 minute și un refresh token de 7 zile, rotit la fiecare folosire și ținut într-un cookie `HttpOnly`.

## Motiv
- Prima versiune nu are integrări externe.
- Access token-ul stă doar în memorie în frontend, niciodată în `localStorage`.
- Rotirea permite detectarea furtului: refolosirea unui token invalidat revocă toată familia.
- Refresh token-urile se stochează hash-uite (SHA-256), iar parolele cu `PasswordHasher<User>` (PBKDF2).
- Schimbarea parolei revocă toate refresh token-urile și pornește o sesiune nouă pentru dispozitivul curent, într-o singură salvare. O sesiune furată nu supraviețuiește, iar utilizatorul nu trebuie să intre din nou în cont.
- Cât timp parola trebuie schimbată, token-ul are claim-ul `must_change_password` și deschide doar schimbarea parolei, `/api/auth/me` și endpoint-urile publice. Restul răspund 403 cu `Auth.PasswordChangeRequired`.
- Un login refuzat are un singur răspuns, și pentru cont inexistent, dezactivat sau blocat, cu același timp de răspuns, ca să nu se poată afla ce conturi există. Motivul real apare doar în loguri.
- Limita de 5 încercări pe minut se aplică pe adresa clientului, citită din `X-Forwarded-For` doar pentru conexiuni din rețele private, unde stă nginx. Altfel toți utilizatorii ar împărți limita lui nginx.

## Alternative
- ASP.NET Core Identity complet: din el avem nevoie doar de `PasswordHasher<User>`.
- Active Directory: ar fi o integrare externă.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/RBAC pe două niveluri|RBAC pe două niveluri]]
