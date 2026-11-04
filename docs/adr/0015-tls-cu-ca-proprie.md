# 0015. TLS cu CA proprie

## Decizie
HTTPS se termină în nginx, cu un certificat de server semnat de o CA proprie. Colegii importă o singură dată `ca.crt`.

## Motiv
- Nu depindem de un certificat de la IT.
- Certificatul serverului se poate reînnoi fără ca ei să mai importe ceva.
- Cu HTTPS valid în browser, cookie-ul de refresh rămâne `Secure; SameSite=Strict`, fără excepții.

## Alternative
- Certificat de la IT: dacă apare mai târziu, se înlocuiesc doar `server.crt` și `server.key`, nimic în cod.
- Certificat self-signed fără CA: fiecare reînnoire ar cere un nou import la toți colegii.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
