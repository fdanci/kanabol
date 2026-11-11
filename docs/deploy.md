# Instalarea pe server

Pașii de mai jos pun Kanabol pe serverul din rețeaua internă și îl țin la zi. Serverul are o clonă a repo-ului și își construiește singur imaginile, fără registry (`docs/SPEC.md` §13.5). Toate comenzile rulează pe server, din rădăcina clonei.

## Ce trebuie pe server

- Linux cu Docker Engine și pluginul `docker compose` (v2.24 sau mai nou), `git`, `openssl` și `cron`.
- Porturile 80 și 443 libere. Pe 80 nginx doar trimite spre HTTPS.
- Un nume sau o adresă fixă prin care colegii ajung la server, de exemplu `kanabol.local` și `10.0.0.10`.
- Utilizatorul care rulează pașii are voie să folosească Docker și `git config user.email` setat, pentru tag-urile pe care le pune `release.sh`.

## Prima instalare

### 1. Clona

```sh
git clone <adresa repo-ului> /srv/kanabol
cd /srv/kanabol
git checkout main
```

`/srv/kanabol` e doar un exemplu; restul pașilor rulează din folderul clonei.

### 2. `.env`

```sh
cp .env.example .env
chmod 600 .env
```

Apoi completează fiecare valoare în `.env`:

- `POSTGRES_PASSWORD`: parola bazei de date, de exemplu din `openssl rand -base64 24`.
- `JWT_SECRET`: cheia care semnează sesiunile, din `openssl rand -base64 48`.
- `SEQ_ADMIN_HASH`: hash-ul parolei de admin din Seq, nu parola. Se face cu `echo '<parola>' | docker run --rm -i datalust/seq config hash`.
- `ADMIN_INITIAL_PASSWORD`: parola primului cont, `admin@kanabol.local`, de cel puțin 10 caractere. La prima intrare contul o schimbă.

`KANABOL_VERSION` îl scrie `release.sh` la pasul 4. `.env` rămâne doar pe server: git îl ignoră.

### 3. Certificatele

Cu toate numele și adresele prin care colegii ajung la server:

```sh
scripts/make-certs.sh kanabol.local kanabol 10.0.0.10
```

Scriptul face în `certs/` CA-ul propriu și certificatul serverului. `docs/tls.md` spune ce fișier pleacă la colegi și ce nu părăsește serverul.

### 4. Imaginile

```sh
scripts/release.sh v1.1.0
```

Versiunea e cea a commit-ului din `main`, ultimul tag (`git describe --tags`): `APP_VERSION` din `frontend/src/app/core/layout/sidenav.ts`, pe care o arată sidebar-ul, și tag-ul implicit al imaginilor din `compose.prod.yaml`. `release.sh` verifică întâi că clona e pe `main`, curată și la un commit de pe `origin/main`, și că versiunea cerută e cea din cele două fișiere, apoi construiește `kanabol-api:v1.1.0` și `kanabol-web:v1.1.0`, pune tag-ul git adnotat dacă lipsește (`v1.1.0` vine deja cu `git pull`) și scrie `KANABOL_VERSION=v1.1.0` în `.env`. Nu pornește nimic: la final spune comanda de la pasul următor. Prima construcție durează câteva minute.

### 5. Pornirea

```sh
docker compose -f compose.yaml -f compose.prod.yaml up -d
docker compose -f compose.yaml -f compose.prod.yaml ps
```

La prima pornire API-ul face baza de date și contul `admin@kanabol.local`, cu parola din `ADMIN_INITIAL_PASSWORD`. Deschide `https://kanabol.local` (după pasul 8 browserul nu mai avertizează), intră cu acest cont și alege parola nouă.

Containerele repornesc singure după un restart al serverului (`restart: unless-stopped`).

### 6. Retenția Seq

Seq păstrează logurile în volumul lui și trebuie să le șteargă după 30 de zile. Setarea se face o dată pentru fiecare volum nou. În producție Seq nu are port publicat, deci scriptul îl atinge la adresa containerului, de pe server:

```sh
seq_ip=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' \
  "$(docker compose -f compose.yaml -f compose.prod.yaml ps -q seq)")
scripts/seq-retention.sh "http://$seq_ip"
```

Scriptul cere parola de admin din Seq, cea din care ai făcut `SEQ_ADMIN_HASH`. Interfața Seq se vede de pe un calculator al echipei printr-un tunel SSH: `ssh -L 5341:<seq_ip>:80 <server>`, apoi `http://localhost:5341`.

### 7. Backup-ul de noapte

Linia de cron și restaurarea sunt în `docs/backup.md`:

```cron
0 2 * * * cd /srv/kanabol && scripts/backup.sh >/dev/null 2>&1
```

### 8. `ca.crt` la colegi

Fiecare coleg importă o dată `certs/ca.crt`, după `docs/tls.md`. `certs/ca.key` nu pleacă de pe server.

## Actualizarea la o versiune nouă

Pe calculatorul de lucru, versiunea nouă intră în `main` după gate (`docs/SPEC.md` §14), cu tag-ul ei împins. Commit-ul pe care îl etichetează tag-ul schimbă versiunea, în același commit, în două locuri (`docs/SPEC.md` §13.5): `APP_VERSION` din `frontend/src/app/core/layout/sidenav.ts`, pe care o arată sidebar-ul, și tag-ul implicit al ambelor imagini din `compose.prod.yaml`:

```text
export const APP_VERSION = '0.2.0';
```

```text
    image: kanabol-api:${KANABOL_VERSION:-v0.2.0}
    image: kanabol-web:${KANABOL_VERSION:-v0.2.0}
```

`scripts/check-docker.sh 3` și `scripts/check-release.sh 2` pică dacă cele două nu se potrivesc. Tag-ul se pune apoi pe commit-ul din `main` care le are:

```sh
git tag -a v0.2.0 -m "Kanabol v0.2.0"
git push origin main v0.2.0
```

Pe server:

```sh
scripts/backup.sh
git pull
scripts/release.sh v0.2.0
docker compose -f compose.yaml -f compose.prod.yaml up -d
```

`up -d` reface doar containerele `api` și `web`, cu imaginile noi; API-ul aplică singur migrările noi la pornire. Datele rămân în volume.

Fără tag-ul împins, `release.sh` îl pune singur pe server și spune cum se împinge. O versiune pe care `APP_VERSION` sau `compose.prod.yaml` n-o au o refuză înainte să construiască, să pună tag-ul sau să schimbe `.env`: un tag pus pe un commit care n-a schimbat versiunea nu se instalează, deci versiunea se schimbă în commit-ul etichetat, nu după el.

### Întoarcerea la versiunea de dinainte

Imaginile vechi rămân pe server (`docker images kanabol-api`). O versiune nouă poate să fi schimbat baza de date, deci întoarcerea merge cu backup-ul făcut înainte de actualizare:

1. În `.env`, `KANABOL_VERSION` înapoi la versiunea veche, de exemplu `v0.1.2`.
2. `scripts/restore.sh backups/kanabol-AAAA-LL-ZZ`, cu folderul de dinainte de actualizare. Scriptul pornește stack-ul cu versiunea din `.env`.

Imaginile de care nu mai e nevoie se șterg cu `docker rmi kanabol-api:v0.1.2 kanabol-web:v0.1.2`.

## Date de demonstrație

Pentru o instalare de probă sau o demonstrație, `scripts/demo-data.sh` umple Kanabol prin API: trei colegi (Ana Popescu, Dan Ionescu și Ioana Marin, cu adrese `@kanabol.local`), rolurile „Manager de proiect” și „Membru”, proiectele „Portal clienți” (`PORT`, cu o coloană „În review”) și „Aplicația de teren” (`TER`), 15 task-uri cu deadline-uri, unele întârziate și unele terminate, comentarii, un fișier și notificările pe care le fac. Nu e pentru un server cu munca reală a echipei.

Contul de admin trebuie să-și fi schimbat deja prima parolă. Parolele vin din variabile de mediu, deci scriptul nu întreabă nimic și nu le scrie nicăieri:

```sh
read -rs KANABOL_ADMIN_PASSWORD && export KANABOL_ADMIN_PASSWORD
read -rs KANABOL_DEMO_PASSWORD && export KANABOL_DEMO_PASSWORD
KANABOL_CACERT=certs/ca.crt scripts/demo-data.sh https://kanabol.local
```

`KANABOL_DEMO_PASSWORD` (cel puțin 10 caractere) e parola cu care intră Ana și Dan. Ioana o primește ca parolă inițială și o schimbă la prima intrare. Fără adresă, scriptul merge la `http://localhost:4200`, aplicația pornită local cu `ng serve`.

Rulat a doua oară, scriptul nu face nimic de două ori: găsește ce există după email, numele rolului, cheia proiectului și cheia task-ului, și adaugă doar ce lipsește.
