# Backup și restaurare

Serverul face în fiecare noapte o copie a bazei de date și a fișierelor atașate. O copie se poate întoarce peste stack-ul care rulează, sau într-un stack nou, pe alt server. Deciziile sunt în `docs/SPEC.md` §13.5.

## Ce face `scripts/backup.sh`

Scriptul rulează pe server, din rădăcina clonei, pe stack-ul pornit cu `compose.yaml` și `compose.prod.yaml` (`docs/deploy.md`):

- baza de date: `pg_dump -Fc` rulat ca utilizatorul `postgres` din containerul PostgreSQL, în `backups/kanabol-AAAA-LL-ZZ/db.dump`. Conexiunile locale din container nu cer parolă, deci nicio parolă nu apare pe linia de comandă sau în log;
- fișierele atașate: volumul `attachments`, citit printr-un container `alpine`, în `backups/kanabol-AAAA-LL-ZZ/attachments.tar.gz`;
- păstrează 14 zile: șterge folderele `backups/kanabol-AAAA-LL-ZZ` cu o dată de acum mai bine de 14 zile, doar după un backup reușit; orice altceva din `backups/` rămâne;
- scrie un rând pe rulare în `backups/backup.log`, `ok` cu mărimile fișierelor sau `FAIL` cu motivul.

Un backup care pică (de exemplu, cu PostgreSQL oprit) nu atinge nimic: backup-ul zilei, dacă există, și cele vechi rămân cum erau. Rulat de două ori în aceeași zi, al doilea îl înlocuiește pe primul.

Seq și logurile lui nu intră în backup.

`backups/` e în `.gitignore`. Copiile țin datele tuturor, deci folderul trebuie să rămână doar pe server, sau copiat pe un alt disc cu aceleași drepturi.

## Cron-ul de la 02:00

`crontab -e` pentru utilizatorul care rulează Docker pe server, cu calea reală a clonei:

```cron
0 2 * * * cd /srv/kanabol && scripts/backup.sh >/dev/null 2>&1
```

Rezultatul fiecărei nopți e în `backups/backup.log`:

```text
2026-11-10 02:00:07 ok backups/kanabol-2026-11-10 (db.dump 1843210 bytes, attachments.tar.gz 5120344 bytes; removed 1 older than 14 days)
```

O zi fără rând înseamnă că nu a rulat cron-ul. Un backup se poate face oricând și de mână, de exemplu înainte de o actualizare:

```sh
scripts/backup.sh
```

## Restaurarea

`scripts/restore.sh` întoarce baza de date și fișierele la momentul unui backup. Tot ce s-a salvat după el se pierde, inclusiv parolele schimbate și sesiunile deschise, deci colegii intră din nou în cont.

```sh
scripts/restore.sh backups/kanabol-2026-11-09
```

Scriptul spune ce urmează să facă și așteaptă `yes`. Orice alt răspuns oprește totul, fără nicio schimbare. Cu `--yes` nu mai întreabă:

```sh
scripts/restore.sh backups/kanabol-2026-11-09 --yes
```

Pașii:

1. Pornește PostgreSQL, dacă nu rulează, și verifică amândouă fișierele copiei.
2. Oprește `api` și `web`, deci Kanabol nu răspunde cât durează restaurarea.
3. Despachetează fișierele în volum, lângă cele vechi.
4. Întoarce baza de date cu `pg_restore --clean --if-exists`, într-o singură tranzacție.
5. Înlocuiește fișierele vechi cu cele despachetate, apoi pornește tot stack-ul și așteaptă ca API-ul să fie sănătos.

Un pas care pică înainte de al cincilea lasă baza de date și fișierele cum erau și pornește din nou stack-ul.

### Pe un server nou

Restaurarea merge și într-un stack nou, cu baza de date și volumul goale. Pe serverul nou, după pașii din `docs/deploy.md` până la `compose up` (fără el), se copiază folderul backup-ului în `backups/` și:

```sh
scripts/restore.sh backups/kanabol-2026-11-09 --yes
```

Scriptul pornește PostgreSQL, întoarce copia și pornește stack-ul. Contul de admin și parolele sunt cele din backup, iar `ADMIN_INITIAL_PASSWORD` din `.env` nu mai contează. Versiunea Kanabol (`KANABOL_VERSION`) trebuie să fie cea din momentul backup-ului sau una mai nouă: API-ul aplică singur migrările care lipsesc, dar nu le poate desface pe cele mai noi decât el.
