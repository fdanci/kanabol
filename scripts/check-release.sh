#!/bin/sh
# Acceptance checks for the delivery (group 19b in the project's Tasks.md; docs/SPEC.md §13.5, §14.7, §16;
# docs/UI_UX_SPEC.md §15). The server's scripts run here as they will on the server, on stacks of their
# own made from compose.yaml and compose.prod.yaml, with the images built from this checkout.
#
# Usage: scripts/check-release.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  backup.sh and restore.sh, on a probe stack of the production files (its own project, volumes, .env,
#      certificates from make-certs.sh and ports): a project, a task, a comment and a binary file made
#      through the API; a backup with PostgreSQL stopped fails, says so in backups/backup.log and leaves
#      today's folder and the old ones alone; then backup.sh writes backups/kanabol-<today>/ with a pg_dump
#      in custom format and the volume's tar, removes the folders named after a day over 14 days back and
#      keeps the rest, logs one line per run, and nothing it prints or logs holds a password; restore.sh
#      without yes changes nothing; with yes it puts back the database and the volume over newer work (a
#      project, a file and the admin's password changed after the backup are gone), the file comes back
#      byte for byte and the API still saves new ones; restore.sh --yes fills a new stack whose database
#      and volume are empty. Docker.
#   2  release.sh on a clean clone as the server has it after git pull (main, origin/main on it): it
#      refuses a version not vX.Y.Z, changes git doesn't have (tracked or not), another branch, a commit
#      origin/main lacks, a tag of that version on another commit and a missing .env, each before
#      building, tagging or changing anything; then it builds kanabol-api and kanabol-web with the tag and
#      the commit in their labels, puts the annotated tag on HEAD, writes KANABOL_VERSION in .env, once,
#      leaving the rest of it as it was, prints the compose up command and no password, and does the same
#      again for the same version; compose.yaml and compose.prod.yaml then run those images from the
#      KANABOL_VERSION in .env alone, without pulling, the app answering on HTTPS and the API signing in
#      the first admin. Since group 21 (docs/SPEC.md §13.5): this checkout's APP_VERSION and the default
#      tag of compose.prod.yaml are one version, and a version tag on HEAD is that one; the clone's commit
#      sets both to the release's version, as the commit a version tags does; release.sh refuses a
#      version that either doesn't name (another version, kanabol-web's default alone changed, APP_VERSION
#      alone changed), each with no image, no tag and .env unchanged; and the web image it builds holds
#      the version the sidebar shows. Docker.
#   3  the documents and the screenshots: docs/deploy.md, docs/backup.md and docs/ui-review.md tracked; each
#      of the 16 screens of docs/UI_UX_SPEC.md §7 (at least one per section) a tracked PNG of 1440x900 in
#      docs/screens/, light and dark, the two unlike each other, and nothing else there; docs/ui-review.md
#      naming each, axe-core, both themes and the motion; axe-core a dev dependency of the frontend, which
#      the app doesn't import; and the production build's CSS turning every transition to 0 and the
#      skeleton's shimmer off under prefers-reduced-motion (docs/UI_UX_SPEC.md §8). Only the packages.
#   4  demo-data.sh on the real API in Production, on a database of its own: without its passwords it
#      gives its usage and asks nothing; while the admin still has the first password it stops and makes
#      nothing; then it makes 3 users (Ana and Dan sign in with the demo password, Ioana has to change
#      it), 2 projects with their columns (PORT with "În review"), 15 tasks, 14 with a deadline, 4 finished
#      and 3 overdue, 6 comments, a file, and the admin's notifications of 6 assignments and 4 comments;
#      run again it makes nothing, and after the file was deleted it uploads that alone; nothing it prints
#      holds a password. PostgreSQL.
#   5  (group 20; docs/SPEC.md §16) every command of the Application project, found by reflection and as
#      many as the records named ...Command in its code: those with anything but ids, nothing or a
#      deadline have a FluentValidation validator in that project, as the ones the tests know to take
#      text, numbers or lists do; those without one have only ids (Guid ...Id), the refresh token the
#      cookie brings (a key looked up by its hash), a deadline (DueDate), or nothing; the list of those is
#      printed. Only the packages.
#
# Docker builds the images and runs the stacks; everything is removed at the end. PostgreSQL means one whose
# user may create databases, with its connection string in KANABOL_CHECK_DB or else in the API's user
# secrets (ConnectionStrings:Default); each run works in its own database, kanabol_release_check_<pid>, and
# drops it. The real API listens on port 5283.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

# Shells opened before Docker Desktop was installed don't have it on the PATH.
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
# Git Bash would otherwise rewrite container paths like /data/attachments into Windows paths.
docker() {
  MSYS_NO_PATHCONV=1 command docker "$@"
}

failures=0
scratch=$(mktemp -d)
run_id="kanabol-release-$$"
check_tag="check-$$"
probe="$scratch/probe"
server="$scratch/server"
server_project="$run_id-release"
server_started=
release_version="v0.0.$$"
# A version that neither APP_VERSION nor compose.prod.yaml names in the clone.
other_version="v0.0.$(($$ + 1))"
compose_projects=

# The probe stack's secrets, which this script makes up. None of them may show in what the scripts print.
database_password=release-check-database-password
jwt_secret=release-check-secret-with-at-least-32-characters
seq_password=release-check-seq-password
initial_password='o parola initiala de verificare'
admin_email=admin@kanabol.local
admin_password='parola adminului la backup'
later_password='parola adminului dupa backup'

cleanup() {
  command -v stop_api >/dev/null 2>&1 && stop_api
  command -v drop_database >/dev/null 2>&1 && drop_database
  for project in $compose_projects; do
    (cd "$probe" 2>/dev/null && probe_compose "$project" down -v --remove-orphans >/dev/null 2>&1)
  done
  [ -z "$server_started" ] || (cd "$server" && server_compose down -v --remove-orphans >/dev/null 2>&1)
  images=$(docker images -q --filter "reference=kanabol-*:$check_tag"
    docker images -q --filter "reference=kanabol-*:$release_version"
    docker images -q --filter "reference=kanabol-*:$other_version")
  [ -z "$images" ] || docker rmi -f $images >/dev/null 2>&1
  rm -rf "$scratch"
}
trap cleanup EXIT

check() { # check <description> <command...>
  description=$1
  shift
  if "$@" >"$scratch/last-check.log" 2>&1; then
    printf '  ok    %s\n' "$description"
  else
    printf '  FAIL  %s\n' "$description"
    sed 's/^/        /' "$scratch/last-check.log" | tail -n 15
    failures=$((failures + 1))
  fi
}

native() { # native <path>: as Windows programs (docker, node) read it
  cygpath -m "$1" 2>/dev/null || echo "$1"
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

docker_is_running() {
  docker info --format '{{.ServerVersion}}'
}

tracked_and_executable() { # tracked_and_executable <file...>
  for file in "$@"; do
    mode=$(git ls-files -s -- "$file" | cut -c1-6)
    is 100755 "$mode" || {
      echo "$file isn't tracked as executable"
      return 1
    }
  done
}

parses_as_posix_sh() { # parses_as_posix_sh <file...>: dash, the server's /bin/sh, reads them
  for file in "$@"; do
    dash -n "$file" || return 1
  done
}

file_has() { # file_has <file> <fixed text...>: each text is in the file
  file=$1
  shift
  [ -f "$file" ] || {
    echo "no $file"
    return 1
  }
  for text in "$@"; do
    grep -qF -- "$text" "$file" || {
      echo "$file doesn't say: $text"
      return 1
    }
  done
}

no_password_in() { # no_password_in <file...>: none of the probe's secrets shows in them
  for file in "$@"; do
    for secret in "$database_password" "$jwt_secret" "$seq_password" "$initial_password" "$admin_password" \
      "$later_password"; do
      if grep -qF -- "$secret" "$file"; then
        echo "$file holds a password"
        return 1
      fi
    done
  done
}

# ---- A stack of the production files, in a folder laid out as the server's clone is.

# The images, built once from this checkout, under the tag the probe's .env names.
images_are_built() {
  docker image inspect "kanabol-api:$check_tag" >/dev/null 2>&1 ||
    docker build -q -t "kanabol-api:$check_tag" backend >"$scratch/build-api.log" 2>&1 || {
    tail -n 20 "$scratch/build-api.log"
    return 1
  }
  docker image inspect "kanabol-web:$check_tag" >/dev/null 2>&1 ||
    docker build -q -t "kanabol-web:$check_tag" frontend >"$scratch/build-web.log" 2>&1 || {
    tail -n 20 "$scratch/build-web.log"
    return 1
  }
}

seq_hash=
seq_hash_is_known() {
  [ -n "$seq_hash" ] && return 0
  seq_hash=$(echo "$seq_password" | docker run --rm -i datalust/seq config hash | tr -d '\r')
  [ -n "$seq_hash" ]
}

# The files the server's clone needs for the stack and its scripts, with its own .env and certificates.
probe_folder_is_ready() {
  [ -f "$probe/.env" ] && return 0
  mkdir -p "$probe/scripts" "$probe/frontend" || return 1
  cp compose.yaml compose.prod.yaml "$probe/" || return 1
  cp frontend/nginx.prod.conf "$probe/frontend/" || return 1
  cp scripts/backup.sh scripts/restore.sh scripts/make-certs.sh "$probe/scripts/" || return 1
  # Only the ports are the probe's own, so it stays out of the way of whatever runs here.
  cat >"$probe/probe-ports.yaml" <<'EOF'
services:
  web:
    ports: !override
      - "127.0.0.1::443"
EOF
  seq_hash_is_known || return 1
  cat >"$probe/.env" <<EOF
POSTGRES_PASSWORD=$database_password
JWT_SECRET=$jwt_secret
SEQ_ADMIN_HASH=$seq_hash
ADMIN_INITIAL_PASSWORD=$initial_password
KANABOL_VERSION=$check_tag
EOF
  (cd "$probe" && sh scripts/make-certs.sh localhost 127.0.0.1) >"$scratch/certs.log" 2>&1 || {
    cat "$scratch/certs.log"
    return 1
  }
}

# compose in the probe folder, as the stack given: COMPOSE_FILE is how the scripts are pointed at it.
probe_compose() { # probe_compose <project> <arguments...>
  project=$1
  shift
  COMPOSE_PROJECT_NAME=$project COMPOSE_FILE=compose.yaml:compose.prod.yaml:probe-ports.yaml \
    COMPOSE_PATH_SEPARATOR=: docker compose "$@"
}

in_probe() { # in_probe <project> <arguments...>: probe_compose from the probe folder
  project=$1
  shift
  case " $compose_projects " in
    *" $project "*) ;;
    *) compose_projects="$compose_projects $project" ;;
  esac
  (cd "$probe" && probe_compose "$project" "$@")
}

probe_script() { # probe_script <project> <script> [arguments...]: one of the server's scripts, on that stack
  project=$1
  shift
  case " $compose_projects " in
    *" $project "*) ;;
    *) compose_projects="$compose_projects $project" ;;
  esac
  script=$1
  shift
  COMPOSE_PROJECT_NAME=$project COMPOSE_FILE=compose.yaml:compose.prod.yaml:probe-ports.yaml \
    COMPOSE_PATH_SEPARATOR=: dash "$probe/scripts/$script" "$@"
}

stack_comes_up() { # stack_comes_up <project>
  images_are_built || return 1
  probe_folder_is_ready || return 1
  in_probe "$1" up -d --wait --wait-timeout 240 >"$scratch/up-$1.log" 2>&1 || {
    tail -n 20 "$scratch/up-$1.log"
    in_probe "$1" logs --tail 20 api 2>&1
    return 1
  }
  set_url "$1"
}

set_url() { # set_url <project>: where that stack's nginx answers
  web_url="https://$(in_probe "$1" port web 443 | tr -d '\r')"
}

volume_files() { # volume_files <project>: the files in the attachments volume, with their sizes, sorted
  api_container=$(in_probe "$1" ps -a -q api | tr -d '\r' | head -n 1)
  [ -n "$api_container" ] || return 1
  docker run --rm --volumes-from "$api_container" alpine:3.21 \
    sh -c 'cd /data/attachments && find . -type f -exec stat -c "%n %s %u" {} + | sort'
}

started_at() { # started_at <project> <service>
  docker inspect -f '{{.State.StartedAt}}' "$(in_probe "$1" ps -a -q "$2" | tr -d '\r' | head -n 1)"
}

# ---- Requests to the probe's API, through its nginx.

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  rm -f "$scratch/body"
  # -k: the certificate is the probe's own, and check-docker.sh is where TLS is judged.
  curl -sSk --max-time 60 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$web_url$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    # From a file: Windows hands curl its arguments in the ANSI code page, where ă turns into a.
    printf '%s' "$4" >"$scratch/request.json"
    send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
      --data-binary "@$(native "$scratch/request.json")"
  else
    send "$method" "$path" -H "Authorization: Bearer $token"
  fi
}

body_field() { # body_field <name>: the value of the first text field with that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body" | head -n 1
}

status_is() { # status_is <expected> <actual>: shows the answer when it differs
  is "$1" "$2" || {
    head -c 600 "$scratch/body" 2>/dev/null
    return 1
  }
}

login() { # login <email> <password>: leaves the token in token
  printf '{"email":"%s","password":"%s"}' "$1" "$2" >"$scratch/login.json"
  status=$(send POST /api/auth/login -H 'Content-Type: application/json' --data-binary "@$(native "$scratch/login.json")")
  status_is 200 "$status" || return 1
  token=$(body_field accessToken)
}

sha_of_download() { # sha_of_download <token> <attachment id>
  rm -f "$scratch/download"
  status=$(curl -sSk --max-time 60 -o "$scratch/download" -w '%{http_code}' -H "Authorization: Bearer $1" \
    "$web_url/api/attachments/$2")
  is 200 "$status" || return 1
  sha256sum "$scratch/download" | cut -d' ' -f1
}

upload() { # upload <token> <task id> <file>: leaves the attachment's id in uploaded
  status=$(send POST "/api/tasks/$2/attachments" -H "Authorization: Bearer $1" -F "file=@$(native "$3")")
  status_is 201 "$status" || return 1
  uploaded=$(body_field id)
}

# ---- Task 1: backup.sh and restore.sh.

today=$(date +%Y-%m-%d)
day_back() { # day_back <days>
  date -d "@$(($(date +%s) - $1 * 86400))" +%Y-%m-%d
}

# What the first stack holds before the backup, made through the API.
probe_has_data() {
  login "$admin_email" "$initial_password" || return 1
  printf '{"currentPassword":"%s","newPassword":"%s"}' "$initial_password" "$admin_password" >"$scratch/password.json"
  status=$(send POST /api/auth/change-password -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
    --data-binary "@$(native "$scratch/password.json")")
  status_is 200 "$status" || return 1
  token=$(body_field accessToken)
  admin_token=$token
  status=$(as "$token" POST /api/projects '{"name":"Copie de siguranță","key":"BKP","description":"Ce trebuie să rămână."}')
  status_is 201 "$status" || return 1
  backup_project=$(body_field id)
  status=$(as "$token" POST "/api/projects/$backup_project/tasks" '{"title":"De păstrat în backup","description":null,"priority":"High"}')
  status_is 201 "$status" || return 1
  backup_task=$(body_field id)
  status=$(as "$token" POST "/api/tasks/$backup_task/comments" '{"body":"Scris înainte de backup."}')
  status_is 201 "$status" || return 1
  # Every byte value, CR and LF among them, so a stream that changed any would show.
  node -e "const b = Buffer.alloc(300000); for (let i = 0; i < b.length; i++) b[i] = (i * 7919) % 256; require('fs').writeFileSync(process.argv[1], b)" \
    "$(native "$scratch/before.zip")" || return 1
  upload "$token" "$backup_task" "$scratch/before.zip" || return 1
  kept_file=$uploaded
  kept_sha=$(sha256sum "$scratch/before.zip" | cut -d' ' -f1)
  is "$kept_sha" "$(sha_of_download "$token" "$kept_file")"
}

old_backups_are_there() { # Folders as earlier days' runs would have left them, and things that aren't backups.
  for days in 30 15 14 3; do
    mkdir -p "$probe/backups/kanabol-$(day_back "$days")" || return 1
    echo "from $days days ago" >"$probe/backups/kanabol-$(day_back "$days")/db.dump"
  done
  mkdir -p "$probe/backups/kanabol-manual" "$probe/backups/other-2020-01-01" || return 1
}

failed_backup_changes_nothing() {
  in_probe "$run_id-a" stop postgres >/dev/null 2>&1 || return 1
  probe_script "$run_id-a" backup.sh >"$scratch/backup-failed.log" 2>&1
  status=$?
  in_probe "$run_id-a" start postgres >/dev/null 2>&1
  # The API's pooled connections died with PostgreSQL, and the first request would get a 500 (Blocked).
  in_probe "$run_id-a" up -d --wait postgres >/dev/null 2>&1 && in_probe "$run_id-a" restart api >/dev/null 2>&1 &&
    in_probe "$run_id-a" up -d --wait >/dev/null 2>&1
  [ "$status" -ne 0 ] || {
    echo "backup.sh exited 0 with PostgreSQL stopped"
    return 1
  }
  [ ! -e "$probe/backups/kanabol-$today" ] || {
    echo "it left a folder for today"
    return 1
  }
  ls -A "$probe/backups" | grep -q partial && {
    echo "it left a partial folder"
    return 1
  }
  [ -d "$probe/backups/kanabol-$(day_back 30)" ] || {
    echo "it removed an old backup although today's failed"
    return 1
  }
  is 1 "$(grep -c ' FAIL postgres' "$probe/backups/backup.log")"
}

backup_writes_today() {
  in_probe "$run_id-a" up -d --wait postgres >/dev/null 2>&1 || return 1
  probe_script "$run_id-a" backup.sh >"$scratch/backup.log" 2>&1 || {
    cat "$scratch/backup.log"
    return 1
  }
  [ -f "$probe/backups/kanabol-$today/db.dump" ] && [ -f "$probe/backups/kanabol-$today/attachments.tar.gz" ] || {
    ls -la "$probe/backups/kanabol-$today"
    return 1
  }
  is PGDMP "$(head -c 5 "$probe/backups/kanabol-$today/db.dump")"
}

dump_holds_the_data() {
  in_probe "$run_id-a" exec -T postgres pg_restore -l <"$probe/backups/kanabol-$today/db.dump" >"$scratch/toc" || return 1
  for table in users projects task_items comments attachments __EFMigrationsHistory; do
    grep -q "TABLE DATA public $table " "$scratch/toc" || {
      echo "no data of $table in the dump"
      return 1
    }
  done
}

tar_holds_the_volume() {
  docker run --rm -i alpine:3.21 sh -c 'tar xzf - -C /tmp && cd /tmp && find . -type f -exec stat -c "%n %s %u" {} + | sort' \
    <"$probe/backups/kanabol-$today/attachments.tar.gz" >"$scratch/in-tar" || return 1
  volume_files "$run_id-a" >"$scratch/in-volume" || return 1
  [ -s "$scratch/in-volume" ] || {
    echo "the volume holds no file"
    return 1
  }
  diff "$scratch/in-volume" "$scratch/in-tar"
}

retention_keeps_14_days() {
  for days in 30 15; do
    [ ! -e "$probe/backups/kanabol-$(day_back "$days")" ] || {
      echo "kanabol-$(day_back "$days") is still there"
      return 1
    }
  done
  for kept in "kanabol-$(day_back 14)" "kanabol-$(day_back 3)" kanabol-manual other-2020-01-01; do
    [ -d "$probe/backups/$kept" ] || {
      echo "$kept was removed"
      return 1
    }
  done
}

log_has_one_line_per_run() {
  is 2 "$(wc -l <"$probe/backups/backup.log" | tr -d ' ')" || return 1
  tail -n 1 "$probe/backups/backup.log" | grep -qE "^$today [0-9:]{8} ok backups/kanabol-$today \(db.dump [0-9]+ bytes, attachments.tar.gz [0-9]+ bytes; removed 2 older than 14 days\)$" || {
    tail -n 1 "$probe/backups/backup.log"
    return 1
  }
}

# Work after the backup, which a restore has to take away.
work_after_the_backup() {
  status=$(as "$admin_token" POST /api/projects '{"name":"După backup","key":"AFTR","description":null}')
  status_is 201 "$status" || return 1
  printf 'scris dupa backup\r\n' >"$scratch/after.txt"
  upload "$admin_token" "$backup_task" "$scratch/after.txt" || return 1
  later_file=$uploaded
  printf '{"currentPassword":"%s","newPassword":"%s"}' "$admin_password" "$later_password" >"$scratch/password.json"
  status=$(send POST /api/auth/change-password -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' \
    --data-binary "@$(native "$scratch/password.json")")
  status_is 200 "$status"
}

restore_without_yes_changes_nothing() {
  before=$(started_at "$run_id-a" api)
  echo no | probe_script "$run_id-a" restore.sh "backups/kanabol-$today" >"$scratch/restore-no.log" 2>&1 && {
    echo "restore.sh exited 0 on no"
    return 1
  }
  probe_script "$run_id-a" restore.sh "backups/kanabol-$today" </dev/null >>"$scratch/restore-no.log" 2>&1 && {
    echo "restore.sh exited 0 without an answer"
    return 1
  }
  grep -q 'Type yes to go on' "$scratch/restore-no.log" || {
    cat "$scratch/restore-no.log"
    return 1
  }
  probe_script "$run_id-a" restore.sh backups/kanabol-nothing --yes >>"$scratch/restore-no.log" 2>&1 && {
    echo "restore.sh exited 0 on a folder that isn't there"
    return 1
  }
  is "$before" "$(started_at "$run_id-a" api)" || return 1
  login "$admin_email" "$later_password" || return 1
  status=$(as "$token" GET '/api/projects?includeArchived=true&pageSize=100')
  status_is 200 "$status" && grep -q '"key":"AFTR"' "$scratch/body"
}

restore_with_yes_puts_it_back() {
  before=$(started_at "$run_id-a" api)
  web_before=$(started_at "$run_id-a" web)
  echo yes | probe_script "$run_id-a" restore.sh "backups/kanabol-$today/" >"$scratch/restore.log" 2>&1 || {
    tail -n 20 "$scratch/restore.log"
    return 1
  }
  [ "$before" != "$(started_at "$run_id-a" api)" ] && [ "$web_before" != "$(started_at "$run_id-a" web)" ] || {
    echo "api and web weren't restarted"
    return 1
  }
  set_url "$run_id-a"
}

stack_serves_the_backup() { # stack_serves_the_backup <project>
  login "$admin_email" "$later_password" >/dev/null 2>&1 && {
    echo "the password set after the backup still signs in"
    return 1
  }
  login "$admin_email" "$admin_password" || return 1
  status=$(as "$token" GET '/api/projects?includeArchived=true&pageSize=100')
  status_is 200 "$status" || return 1
  grep -q '"key":"BKP"' "$scratch/body" || {
    echo "no BKP"
    return 1
  }
  if grep -q '"key":"AFTR"' "$scratch/body"; then
    echo "AFTR, made after the backup, is still there"
    return 1
  fi
  status=$(as "$token" GET "/api/tasks/$backup_task/comments")
  status_is 200 "$status" && grep -q 'Scris' "$scratch/body" || return 1
  is "$kept_sha" "$(sha_of_download "$token" "$kept_file")" || return 1
  status=$(as "$token" GET "/api/attachments/$later_file")
  status_is 404 "$status" || return 1
  volume_files "$1" >"$scratch/restored-volume" || return 1
  diff "$scratch/in-tar" "$scratch/restored-volume"
}

api_still_saves_files() { # api_still_saves_files <project>: the volume kept the owner the API writes as
  printf 'dupa restaurare' >"$scratch/restored.txt"
  upload "$token" "$backup_task" "$scratch/restored.txt" || return 1
  is "$(sha256sum "$scratch/restored.txt" | cut -d' ' -f1)" "$(sha_of_download "$token" "$uploaded")"
}

new_stack_is_empty() {
  in_probe "$run_id-b" up -d --wait postgres >"$scratch/up-b.log" 2>&1 || {
    cat "$scratch/up-b.log"
    return 1
  }
  is 0 "$(in_probe "$run_id-b" exec -T postgres psql -U kanabol -d kanabol -tAc \
    "select count(*) from information_schema.tables where table_schema = 'public'" | tr -d '\r')" || return 1
  is '' "$(in_probe "$run_id-b" ps -a -q api | tr -d '\r')"
}

restore_fills_a_new_stack() {
  probe_script "$run_id-b" restore.sh "backups/kanabol-$today" --yes >"$scratch/restore-new.log" 2>&1 || {
    tail -n 20 "$scratch/restore-new.log"
    return 1
  }
  set_url "$run_id-b"
}

docs_explain_the_backup() {
  file_has docs/backup.md '0 2 * * *' scripts/backup.sh backups/backup.log scripts/restore.sh --yes '14' \
    backups/kanabol-
}

task_1() {
  echo "19b.1 backup.sh and restore.sh"
  check "backup.sh and restore.sh are tracked as executable" tracked_and_executable scripts/backup.sh scripts/restore.sh
  check "dash, the server's /bin/sh, reads them" parses_as_posix_sh scripts/backup.sh scripts/restore.sh
  check "neither reads a password" sh -c '! grep -nE "PASSWORD|PGPASS|[.]env" scripts/backup.sh scripts/restore.sh'
  check "git ignores backups/" git check-ignore -q backups/kanabol-2026-01-01/db.dump
  check "docs/backup.md gives the cron line at 02:00, the log and the restore" docs_explain_the_backup
  check "Docker is running" docker_is_running
  check "a probe stack of compose.yaml and compose.prod.yaml comes up" stack_comes_up "$run_id-a"
  check "it holds a project, a task, a comment and a binary file, made through the API" probe_has_data
  old_backups_are_there
  check "with PostgreSQL stopped the backup fails, logs it and changes nothing" failed_backup_changes_nothing
  check "backup.sh writes backups/kanabol-$today/ with a custom-format dump" backup_writes_today
  check "the dump holds the tables' data" dump_holds_the_data
  check "the tar holds every file of the volume, as it is" tar_holds_the_volume
  check "folders of over 14 days ago are gone, the rest stays" retention_keeps_14_days
  check "backup.log has a line per run, the last one ok with the sizes" log_has_one_line_per_run
  check "nothing backup.sh printed or logged holds a password" no_password_in "$scratch/backup.log" \
    "$scratch/backup-failed.log" "$probe/backups/backup.log"
  check "a project, a file and a new admin password come after the backup" work_after_the_backup
  check "restore.sh without yes, or on a missing folder, changes nothing" restore_without_yes_changes_nothing
  check "restore.sh with yes restarts api and web" restore_with_yes_puts_it_back
  check "the stack serves the backup again, the newer work gone, the file as it was" stack_serves_the_backup "$run_id-a"
  check "the API still saves a file in the volume" api_still_saves_files
  check "a new stack's database and volume are empty" new_stack_is_empty
  check "restore.sh --yes fills it" restore_fills_a_new_stack
  check "it serves the backup, the file as it was" stack_serves_the_backup "$run_id-b"
  check "and saves new files" api_still_saves_files
  check "nothing restore.sh printed holds a password" no_password_in "$scratch/restore.log" \
    "$scratch/restore-new.log" "$scratch/restore-no.log"
}


# ---- Task 2: release.sh.

# The version the sidebar shows and the one compose.prod.yaml runs without KANABOL_VERSION, which the
# commit a version tags sets (docs/SPEC.md §13.5).
app_version_in() { # app_version_in <folder>: APP_VERSION, X.Y.Z
  sed -n "s/^export const APP_VERSION = '\([^']*\)';\$/\1/p" "$1/frontend/src/app/core/layout/sidenav.ts" | tr -d '\r'
}

default_tags_in() { # default_tags_in <folder>: compose.prod.yaml's default tag of each image, "api:vX.Y.Z web:vX.Y.Z"
  sed -n -E 's/^ +image: kanabol-(api|web):\$\{KANABOL_VERSION:-([^}]*)\}$/\1:\2/p' "$1/compose.prod.yaml" |
    tr -d '\r' | sort | tr '\n' ' ' | sed 's/ $//'
}

set_version() { # set_version <folder> <X.Y.Z>: APP_VERSION and both default tags at that version
  sed -i "s/^export const APP_VERSION = '[^']*';\$/export const APP_VERSION = '$2';/" \
    "$1/frontend/src/app/core/layout/sidenav.ts" || return 1
  sed -i -E 's/^( +image: kanabol-(api|web):\$\{KANABOL_VERSION:-)[^}]*\}$/\1v'"$2"'}/' "$1/compose.prod.yaml" || return 1
  is "$2 api:v$2 web:v$2" "$(app_version_in "$1") $(default_tags_in "$1")"
}

checkout_versions_agree() {
  version=$(app_version_in .)
  printf '%s' "$version" | grep -Eq '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' || {
    echo "no X.Y.Z APP_VERSION in frontend/src/app/core/layout/sidenav.ts: '$version'"
    return 1
  }
  is "api:v$version web:v$version" "$(default_tags_in .)" || return 1
  for tag in $(git tag -l --points-at HEAD 'v*'); do
    is "v$version" "$tag" || {
      echo "HEAD is tagged $tag"
      return 1
    }
  done
}

# A clone as the server has it after git pull: main at this checkout's commit, origin/main on it, and the
# release.sh of this checkout's working tree, committed in the clone if it differs, in a commit that sets
# the release's version as the commit a version tags does.
server_clone_is_ready() {
  [ -f "$scratch/server.env" ] && return 0
  git clone -q "$PWD" "$server" || return 1
  (
    cd "$server" || exit 1
    git checkout -q -B main HEAD || exit 1
    git config user.email release-check@kanabol.local && git config user.name 'Release check' || exit 1
    cp "$OLDPWD/scripts/release.sh" scripts/release.sh || exit 1
    set_version . "${release_version#v}" || exit 1
    git add -A && git commit -q --no-verify -m "test: the release.sh of the working tree, at $release_version" || exit 1
    git update-ref refs/remotes/origin/main HEAD
  ) || return 1
  seq_hash_is_known || return 1
  # .env as docs/deploy.md has it made: .env.example, filled in.
  sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$database_password|" \
    -e "s|^JWT_SECRET=.*|JWT_SECRET=$jwt_secret|" \
    -e "s|^SEQ_ADMIN_HASH=.*|SEQ_ADMIN_HASH=$seq_hash|" \
    -e "s|^ADMIN_INITIAL_PASSWORD=.*|ADMIN_INITIAL_PASSWORD=$initial_password|" .env.example >"$server/.env" || return 1
  grep -q '^# KANABOL_VERSION=' "$server/.env" || return 1
  cp "$server/.env" "$scratch/server.env"
}

release() { # release <arguments...>: release.sh in the clone, its output in release.log
  (cd "$server" && env -u KANABOL_VERSION dash scripts/release.sh "$@") >"$scratch/release.log" 2>&1
}

nothing_released() { # Neither image, nor a changed .env.
  if docker image inspect "kanabol-api:$release_version" >/dev/null 2>&1 ||
    docker image inspect "kanabol-web:$release_version" >/dev/null 2>&1; then
    echo "an image was built"
    return 1
  fi
  cmp -s "$server/.env" "$scratch/server.env" || {
    echo ".env changed"
    return 1
  }
}

refuses() { # refuses <expected text> <arguments...>: release.sh exits non-zero, saying so, and does nothing
  expected=$1
  shift
  release "$@" && {
    echo "release.sh $* exited 0"
    cat "$scratch/release.log"
    return 1
  }
  grep -qF -- "$expected" "$scratch/release.log" || {
    cat "$scratch/release.log"
    return 1
  }
  nothing_released || return 1
  # The only tag of that version allowed is the one refuses_a_tag_elsewhere put on the commit before.
  if git -C "$server" rev-parse -q --verify "refs/tags/$release_version" >/dev/null; then
    is "$(git -C "$server" rev-parse HEAD~1)" "$(git -C "$server" rev-list -n 1 "refs/tags/$release_version")" || {
      echo "a tag was made"
      return 1
    }
  fi
}

refuses_bad_versions() {
  for version in 0.1.0 v1.2 v01.2.3 v1.2.3-rc1 'v1.2.3 x'; do
    refuses Usage "$version" || return 1
  done
  refuses Usage
}

refuses_changes() {
  echo '# a change on the server' >>"$server/compose.yaml"
  refuses "changes git doesn't have" "$release_version"
  status=$?
  git -C "$server" checkout -q -- compose.yaml
  [ "$status" -eq 0 ] || return 1
  echo stray >"$server/backend/stray.txt"
  refuses "changes git doesn't have" "$release_version"
  status=$?
  rm -f "$server/backend/stray.txt"
  return "$status"
}

refuses_another_branch() {
  git -C "$server" checkout -q -b elsewhere || return 1
  refuses "not main" "$release_version"
  status=$?
  git -C "$server" checkout -q main
  return "$status"
}

refuses_an_unpushed_commit() {
  git -C "$server" commit -q --allow-empty --no-verify -m 'test: only on the server' || return 1
  refuses "origin/main doesn't" "$release_version"
  status=$?
  git -C "$server" reset -q --hard HEAD~1
  return "$status"
}

refuses_a_tag_elsewhere() {
  git -C "$server" tag -a "$release_version" -m elsewhere HEAD~1 || return 1
  refuses "is on" "$release_version"
  status=$?
  git -C "$server" tag -d "$release_version" >/dev/null
  return "$status"
}

says() { # says <fixed text...>: release.log holds each
  for text in "$@"; do
    grep -qF -- "$text" "$scratch/release.log" || {
      echo "release.sh didn't say: $text"
      cat "$scratch/release.log"
      return 1
    }
  done
}

says_nothing_of() { # says_nothing_of <fixed text>
  ! grep -qF -- "$1" "$scratch/release.log" || {
    cat "$scratch/release.log"
    return 1
  }
}

no_trace_of() { # no_trace_of <version>: neither image nor the git tag of that version
  for part in api web; do
    if docker image inspect "kanabol-$part:$1" >/dev/null 2>&1; then
      echo "kanabol-$part:$1 was built"
      return 1
    fi
  done
  if git -C "$server" rev-parse -q --verify "refs/tags/$1" >/dev/null; then
    echo "the tag $1 was made"
    return 1
  fi
}

# Whatever a release.sh that didn't refuse left, so the checks after start from the same clone.
undo_release() { # undo_release <version>
  git -C "$server" tag -d "$1" >/dev/null 2>&1
  cp "$scratch/server.env" "$server/.env"
  images=$(docker images -q --filter "reference=kanabol-*:$1")
  [ -z "$images" ] || docker rmi -f $images >/dev/null 2>&1
}

commit_on_the_clone() { # commit_on_the_clone <message>: what changed, committed and on origin/main
  git -C "$server" commit -q -a --no-verify -m "$1" || return 1
  git -C "$server" update-ref refs/remotes/origin/main HEAD
}

back_to_the_clones_main() {
  git -C "$server" reset -q --hard HEAD~1 && git -C "$server" update-ref refs/remotes/origin/main HEAD
}

# APP_VERSION and compose.prod.yaml say the release's version: another one is refused, naming both.
refuses_another_version() {
  refuses "APP_VERSION in frontend/src/app/core/layout/sidenav.ts is ${release_version#v}, not ${other_version#v}" \
    "$other_version" &&
    says "compose.prod.yaml runs kanabol-api:$release_version and kanabol-web:$release_version" &&
    no_trace_of "$other_version"
  status=$?
  undo_release "$other_version"
  return "$status"
}

refuses_another_web_default() {
  sed -i -E 's/^( +image: kanabol-web:\$\{KANABOL_VERSION:-)[^}]*\}$/\1'"$other_version"'}/' \
    "$server/compose.prod.yaml" || return 1
  is "api:$release_version web:$other_version" "$(default_tags_in "$server")" || return 1
  commit_on_the_clone "test: kanabol-web's default tag $other_version" || return 1
  refuses "compose.prod.yaml runs kanabol-api:$release_version and kanabol-web:$other_version" "$release_version" &&
    says "not $release_version" && says_nothing_of "APP_VERSION in"
  status=$?
  back_to_the_clones_main || return 1
  undo_release "$release_version"
  return "$status"
}

refuses_another_app_version() {
  sed -i "s/^export const APP_VERSION = '[^']*';\$/export const APP_VERSION = '${other_version#v}';/" \
    "$server/frontend/src/app/core/layout/sidenav.ts" || return 1
  is "${other_version#v}" "$(app_version_in "$server")" || return 1
  commit_on_the_clone "test: APP_VERSION ${other_version#v}" || return 1
  refuses "APP_VERSION in frontend/src/app/core/layout/sidenav.ts is ${other_version#v}, not ${release_version#v}" \
    "$release_version" && says_nothing_of "compose.prod.yaml runs"
  status=$?
  back_to_the_clones_main || return 1
  undo_release "$release_version"
  return "$status"
}

refuses_without_env() {
  mv "$server/.env" "$scratch/env-aside" || return 1
  release "$release_version"
  status=$?
  mv "$scratch/env-aside" "$server/.env"
  [ "$status" -ne 0 ] && grep -qF 'no .env' "$scratch/release.log" || {
    cat "$scratch/release.log"
    return 1
  }
  nothing_released
}

release_builds() {
  release "$release_version" || {
    tail -n 25 "$scratch/release.log"
    return 1
  }
  cp "$scratch/release.log" "$scratch/release-first.log"
}

images_carry_the_release() {
  head=$(git -C "$server" rev-parse HEAD)
  for part in api web; do
    is "$release_version $head" "$(docker image inspect -f \
      '{{index .Config.Labels "org.opencontainers.image.version"}} {{index .Config.Labels "org.opencontainers.image.revision"}}' \
      "kanabol-$part:$release_version" | tr -d '\r')" || return 1
  done
}

image_holds_version() { # image_holds_version <X.Y.Z>: a script of the web image holds it in quotes, next to the sidebar's text
  pattern="[\"'\`]$(printf '%s' "$1" | sed 's/\./\\./g')[\"'\`]"
  # shellcheck disable=SC2016 # the image's shell expands these
  docker run --rm --entrypoint sh "kanabol-web:$release_version" -c \
    'for file in $(grep -rlE "$1" /usr/share/nginx/html); do grep -qF Versiunea "$file" && echo "$file"; done' \
    sh "$pattern" | grep -q .
}

# The sidebar's version is built into the web image: the clone's APP_VERSION, with "Versiunea {{ version }}".
web_image_shows_the_version() {
  image_holds_version "${release_version#v}" || {
    echo "no script of kanabol-web:$release_version holds ${release_version#v}, the clone's APP_VERSION, with the sidebar's text"
    return 1
  }
}

tag_is_annotated_on_head() {
  is tag "$(git -C "$server" cat-file -t "refs/tags/$release_version")" || return 1
  is "$(git -C "$server" rev-parse HEAD)" "$(git -C "$server" rev-list -n 1 "refs/tags/$release_version")"
}

env_has_the_version_once() {
  is 1 "$(grep -c '^KANABOL_VERSION=' "$server/.env")" || return 1
  grep -qx "KANABOL_VERSION=$release_version" "$server/.env" || return 1
  grep -v '^KANABOL_VERSION=' "$server/.env" >"$scratch/env-now"
  diff "$scratch/server.env" "$scratch/env-now"
}

release_says_how_to_start() {
  grep -qF 'docker compose -f compose.yaml -f compose.prod.yaml up -d' "$scratch/release-first.log" || {
    cat "$scratch/release-first.log"
    return 1
  }
}

release_again_changes_nothing() {
  tag_object=$(git -C "$server" rev-parse "refs/tags/$release_version")
  cp "$server/.env" "$scratch/env-before-again"
  release "$release_version" || {
    tail -n 25 "$scratch/release.log"
    return 1
  }
  is "$tag_object" "$(git -C "$server" rev-parse "refs/tags/$release_version")" || return 1
  cmp "$scratch/env-before-again" "$server/.env"
}

server_compose() {
  env -u KANABOL_VERSION docker compose -p "$server_project" -f compose.yaml -f compose.prod.yaml \
    -f release-ports.yaml "$@"
}

release_stack_comes_up() {
  (cd "$server" && sh scripts/make-certs.sh localhost 127.0.0.1) >"$scratch/server-certs.log" 2>&1 || {
    cat "$scratch/server-certs.log"
    return 1
  }
  cat >"$server/release-ports.yaml" <<'EOF'
services:
  web:
    ports: !override
      - "127.0.0.1::443"
EOF
  server_started=1
  # --pull never: the images are the ones release.sh built here, or the start fails.
  (cd "$server" && server_compose up -d --pull never --wait --wait-timeout 240) >"$scratch/server-up.log" 2>&1 || {
    tail -n 20 "$scratch/server-up.log"
    return 1
  }
  web_url="https://$(cd "$server" && server_compose port web 443 | tr -d '\r')"
}

stack_runs_the_release() {
  is "api kanabol-api:$release_version
web kanabol-web:$release_version" \
    "$(cd "$server" && server_compose ps --format '{{.Service}} {{.Image}}' api web | tr -d '\r' | sort)"
}

release_serves_the_app() {
  curl -sSk --max-time 30 "$web_url/projects" | grep -q '<kb-root' || return 1
  login "$admin_email" "$initial_password" || return 1
  grep -q '"mustChangePassword":true' "$scratch/body"
}

docs_explain_the_deploy() {
  file_has docs/deploy.md 'cp .env.example .env' POSTGRES_PASSWORD JWT_SECRET SEQ_ADMIN_HASH ADMIN_INITIAL_PASSWORD \
    scripts/make-certs.sh 'docker compose -f compose.yaml -f compose.prod.yaml up -d' \
    scripts/seq-retention.sh scripts/backup.sh docs/backup.md ca.crt docs/tls.md 'git pull' \
    'scripts/release.sh v0.2.0' KANABOL_VERSION \
    APP_VERSION frontend/src/app/core/layout/sidenav.ts "image: kanabol-api:\${KANABOL_VERSION:-v0.2.0}" || return 1
  # The first install names a version too.
  grep -qE '^scripts/release\.sh v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' docs/deploy.md || {
    echo "docs/deploy.md has no line scripts/release.sh vX.Y.Z"
    return 1
  }
}

task_2() {
  echo "19b.2 release.sh"
  check "release.sh is tracked as executable" tracked_and_executable scripts/release.sh
  check "dash, the server's /bin/sh, reads it" parses_as_posix_sh scripts/release.sh
  check "docs/deploy.md gives the server's steps and the update, the version set in the tagged commit" \
    docs_explain_the_deploy
  check "this checkout's APP_VERSION and compose.prod.yaml's default tag are one version, HEAD's tag too" \
    checkout_versions_agree
  check "Docker is running" docker_is_running
  check "a clean clone is on main, with origin/main on it, at $release_version, and a .env from .env.example" \
    server_clone_is_ready
  check "it refuses a version that isn't vX.Y.Z" refuses_bad_versions
  check "it refuses changes git doesn't have, tracked or not" refuses_changes
  check "it refuses another branch" refuses_another_branch
  check "it refuses a commit origin/main doesn't have" refuses_an_unpushed_commit
  check "it refuses a tag of that version on another commit" refuses_a_tag_elsewhere
  check "it refuses $other_version, which neither APP_VERSION nor compose.prod.yaml names" refuses_another_version
  check "it refuses $release_version when kanabol-web's default tag alone is another" refuses_another_web_default
  check "it refuses $release_version when APP_VERSION alone is another" refuses_another_app_version
  check "it refuses without .env" refuses_without_env
  check "release.sh $release_version builds both images" release_builds
  check "they carry the version and the commit" images_carry_the_release
  check "the web image holds the version for the sidebar" web_image_shows_the_version
  check "the annotated tag is on HEAD" tag_is_annotated_on_head
  check ".env has KANABOL_VERSION once, the rest as it was" env_has_the_version_once
  check "it prints the compose up command" release_says_how_to_start
  check "and no password" no_password_in "$scratch/release-first.log"
  check "the same version again changes nothing else" release_again_changes_nothing
  check "compose.prod.yaml starts them from .env, without pulling" release_stack_comes_up
  check "api and web run the release's images" stack_runs_the_release
  check "the app answers on HTTPS and the first admin signs in" release_serves_the_app
}

# ---- Task 3: the documents and the screenshots.

# Every screen of docs/UI_UX_SPEC.md §7, as docs/ui-review.md lists them, each light and dark.
screens='7.1-login 7.1-parola-noua 7.2-panou 7.2-task-urile-mele 7.3-proiecte 7.3-lista 7.3-membri 7.3-rapoarte
7.3-setari 7.4-board 7.5-task 7.6-notificari-meniu 7.6-notificari 7.7-utilizatori 7.7-roluri 7.8-rapoarte'

documents_are_tracked() {
  for file in docs/deploy.md docs/backup.md docs/ui-review.md; do
    [ -s "$file" ] && git ls-files --error-unmatch "$file" >/dev/null 2>&1 || {
      echo "$file is missing or not tracked"
      return 1
    }
  done
}

png_size() { # png_size <file>: its width x height, from the IHDR chunk
  node -e "const b = require('fs').readFileSync(process.argv[1]);
    if (b.toString('latin1', 1, 4) !== 'PNG' || b.toString('latin1', 12, 16) !== 'IHDR') process.exit(1);
    console.log(b.readUInt32BE(16) + 'x' + b.readUInt32BE(20));" "$(native "$1")"
}

screenshots_are_there() {
  for screen in $screens; do
    for scheme in light dark; do
      file="docs/screens/$screen-$scheme.png"
      git ls-files --error-unmatch "$file" >/dev/null 2>&1 || {
        echo "$file is missing or not tracked"
        return 1
      }
      is 1440x900 "$(png_size "$file")" || {
        echo "in $file"
        return 1
      }
    done
    if cmp -s "docs/screens/$screen-light.png" "docs/screens/$screen-dark.png"; then
      echo "$screen is the same picture in both themes"
      return 1
    fi
  done
}

nothing_else_in_screens() {
  for file in docs/screens/*; do
    name=${file#docs/screens/}
    case " $(echo $screens) " in
      *" ${name%-light.png} "* | *" ${name%-dark.png} "*) ;;
      *)
        echo "$file isn't a screen of the list"
        return 1
        ;;
    esac
  done
}

every_section_has_a_screen() { # Each "### 7.x" of the UI spec has at least one screen, so none is left out.
  sections=$(sed -n 's/^### \(7\.[0-9][0-9]*\) .*/\1/p' docs/UI_UX_SPEC.md)
  [ -n "$sections" ] || return 1
  for section in $sections; do
    case " $(echo $screens) " in
      *" $section-"*) ;;
      *)
        echo "UI §$section has no screenshot"
        return 1
        ;;
    esac
  done
}

review_covers_every_screen() {
  file_has docs/ui-review.md axe-core prefers-reduced-motion light dark Blocked || return 1
  for screen in $screens; do
    grep -qF -- "$screen-" docs/ui-review.md || {
      echo "docs/ui-review.md doesn't name $screen"
      return 1
    }
  done
}

axe_is_a_dev_dependency() {
  node -e "const p = require('./frontend/package.json');
    if (!p.devDependencies?.['axe-core'] || p.dependencies?.['axe-core']) process.exit(1);" || {
    echo "axe-core isn't only in the devDependencies of frontend/package.json"
    return 1
  }
  [ -f frontend/node_modules/axe-core/axe.min.js ] || return 1
  # A tool for the review, never part of the app.
  ! grep -rnE "(from|import\(|require\() *['\"]axe-core" frontend/src
}

# docs/UI_UX_SPEC.md §8, in the CSS the production build ships: under reduce every transition at 0, and the
# skeleton's shimmer off.
reduced_motion_is_built() {
  (cd frontend && npx ng build --configuration production >"$scratch/build.log" 2>&1) || {
    tail -n 20 "$scratch/build.log"
    return 1
  }
  css=$(ls frontend/dist/kanabol/browser/styles-*.css 2>/dev/null | head -n 1)
  [ -n "$css" ] || return 1
  node -e "const css = require('fs').readFileSync(process.argv[1], 'utf8');
    const rule = /@media ?\(prefers-reduced-motion: ?reduce\)\{\*,\*?::?before,\*?::?after\{transition-duration:0m?s!important;transition-delay:0m?s!important\}/;
    if (!rule.test(css)) { console.log(css.match(/@media ?\(prefers-reduced-motion[^}]*\}[^}]*\}/g)); process.exit(1); }" "$(native "$css")" || return 1
  # The skeleton's own rule, from its component's styles in the lazy chunks or main.
  grep -lE 'prefers-reduced-motion: ?reduce\)\{\[_nghost-%COMP%\]\{animation:none;background:var\(' frontend/dist/kanabol/browser/*.js >/dev/null || {
    echo "no skeleton rule stopping its animation under reduce"
    return 1
  }
}

task_3() {
  echo "19b.3 the documents and the screenshots of every screen"
  check "docs/deploy.md, docs/backup.md and docs/ui-review.md are tracked" documents_are_tracked
  check "every screen has its PNG of 1440x900 in docs/screens/, light and dark, unlike each other" screenshots_are_there
  check "docs/screens/ holds nothing else" nothing_else_in_screens
  check "every section of UI §7 has a screenshot" every_section_has_a_screen
  check "docs/ui-review.md names every screen, axe-core, both themes and the motion" review_covers_every_screen
  check "axe-core is a dev dependency of the frontend, which the app doesn't import" axe_is_a_dev_dependency
  check "the production CSS stops every transition and the shimmer under reduce" reduced_motion_is_built
}

# ---- Task 4: demo-data.sh, on the real API (as check-reports.sh starts it).

check_db="kanabol_release_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5283
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
base_connection=
demo_password='parola colegilor de demonstratie'

# Run a statement and print its rows, with columns separated by |. Built once, on first use.
sql() { # sql <connection> <statement>
  sql_dll="$scratch/sql/bin/Debug/net10.0/sql.dll"
  if [ ! -f "$sql_dll" ]; then
    mkdir -p "$scratch/sql"
    cat >"$scratch/sql/sql.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.*" />
  </ItemGroup>
</Project>
XML
    cat >"$scratch/sql/Program.cs" <<'CS'
Console.OutputEncoding = System.Text.Encoding.UTF8;
await using var connection = new Npgsql.NpgsqlConnection(args[0]);
await connection.OpenAsync();
await using var command = new Npgsql.NpgsqlCommand(args[1], connection);
await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)?.ToString());
    Console.WriteLine(string.Join('|', values));
}
CS
    dotnet build "$scratch/sql/sql.csproj" --nologo -v q >"$scratch/sql/build.log" 2>&1 || {
      cat "$scratch/sql/build.log" >&2
      return 1
    }
  fi
  dotnet "$sql_dll" "$1" "$2"
}

need_database() {
  [ -n "$admin_connection" ] && return 0
  base_connection=${KANABOL_CHECK_DB:-$(dotnet user-secrets list --id kanabol-api 2>/dev/null |
    tr -d '\r' | sed -n 's/^ConnectionStrings:Default = //p')}
  if [ -z "$base_connection" ]; then
    echo "No database: set KANABOL_CHECK_DB or the API's ConnectionStrings:Default user secret."
    return 1
  fi
  admin_connection=$(printf '%s' "$base_connection" | sed 's/Database=[^;]*/Database=postgres/')
  sql "$admin_connection" "CREATE DATABASE $check_db"
}

connection_to() { # connection_to <database>
  printf '%s' "$base_connection" | sed "s/Database=[^;]*/Database=$1/"
}

in_db() { # in_db <statement>: its rows, as sql prints them
  sql "$(connection_to "$check_db")" "$1" | tr -d '\r'
}

drop_database() {
  [ -n "$admin_connection" ] || return 0
  sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null 2>&1
}

# The API in Production on this run's database, without the overdue check, which would add notifications.
api_is_up() {
  [ -n "$api_pid" ] && return 0
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q >"$scratch/api-build.log" 2>&1 || {
    tail -n 20 "$scratch/api-build.log"
    return 1
  }
  (
    cd "$api_dir" || exit 1
    # Seq's address leads nowhere, so a developer's own Seq gets nothing of these checks.
    exec env ASPNETCORE_ENVIRONMENT=Production \
      ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret="$jwt_secret" \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword="$initial_password" \
      Alerts__OverdueCheckEnabled=false \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$api_log" 2>&1 &
  api_pid=$!
  attempts=0
  until curl -s -o /dev/null "$API_URL/health"; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 120 ] || ! kill -0 "$api_pid" 2>/dev/null; then
      api_pid=
      tail -n 30 "$api_log"
      return 1
    fi
    sleep 0.5
  done
}

stop_api() {
  [ -n "$api_pid" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

demo_data() { # demo_data <log> [VARIABLE=value...]: demo-data.sh on the real API, never with a terminal
  log=$1
  shift
  env -u KANABOL_ADMIN_EMAIL -u KANABOL_ADMIN_PASSWORD -u KANABOL_DEMO_PASSWORD -u KANABOL_CACERT "$@" \
    dash scripts/demo-data.sh "$API_URL" </dev/null >"$log" 2>&1
}

demo_data_needs_its_passwords() {
  demo_data "$scratch/demo-usage.log"
  is 2 "$?" || return 1
  grep -q '^Usage: ' "$scratch/demo-usage.log" || return 1
  demo_data "$scratch/demo-usage.log" KANABOL_ADMIN_PASSWORD=x KANABOL_DEMO_PASSWORD=short
  is 2 "$?"
}

demo_data_waits_for_the_first_admin() {
  demo_data "$scratch/demo-first.log" KANABOL_ADMIN_PASSWORD="$initial_password" KANABOL_DEMO_PASSWORD="$demo_password"
  is 1 "$?" || return 1
  grep -q 'still has to change' "$scratch/demo-first.log" || {
    cat "$scratch/demo-first.log"
    return 1
  }
  is 1 "$(in_db 'select count(*) from users')"
}

# The check's own logins come from addresses of their own, so demo-data.sh keeps its five a minute.
the_admin_changed_the_password() {
  web_url=$API_URL
  printf '{"email":"%s","password":"%s"}' "$admin_email" "$initial_password" >"$scratch/login.json"
  status=$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.83.0.1' \
    --data-binary "@$(native "$scratch/login.json")")
  status_is 200 "$status" || return 1
  printf '{"currentPassword":"%s","newPassword":"%s"}' "$initial_password" "$admin_password" >"$scratch/password.json"
  status=$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
    -H 'Content-Type: application/json' --data-binary "@$(native "$scratch/password.json")")
  status_is 200 "$status" || return 1
  admin_token=$(body_field accessToken)
}

demo_counts() { # What the demonstration data is made of, one line per table or rule.
  in_db "select 'users', count(*) from users
    union all select 'roles', count(*) from roles
    union all select 'projects', count(*) from projects
    union all select 'columns', count(*) from board_columns
    union all select 'members', count(*) from project_members
    union all select 'tasks', count(*) from task_items
    union all select 'assignees', count(*) from task_assignees
    union all select 'finished', count(*) from task_items where completed_at is not null
    union all select 'overdue', count(*) from task_items where completed_at is null and due_date < now()
    union all select 'comments', count(*) from comments
    union all select 'attachments', count(*) from attachments
    union all select 'notifications', count(*) from notifications
    union all select 'history', count(*) from task_history_entries" >"$scratch/counts" || return 1
  # Every line, so a statement that fails can't make two runs look the same.
  is 13 "$(grep -c '|' "$scratch/counts")" || return 1
  cat "$scratch/counts"
}

run_demo_data() { # run_demo_data <log>
  demo_data "$1" KANABOL_ADMIN_PASSWORD="$admin_password" KANABOL_DEMO_PASSWORD="$demo_password" || {
    tail -n 20 "$1"
    return 1
  }
}

first_run_makes_the_data() {
  run_demo_data "$scratch/demo-1.log" || return 1
  grep -q '^Done: [0-9]* new\.$' "$scratch/demo-1.log" || {
    cat "$scratch/demo-1.log"
    return 1
  }
  demo_counts >"$scratch/counts-1"
}

data_is_what_was_asked() {
  is "users|4
roles|3
projects|2
columns|7
members|8
tasks|15
finished|4
overdue|3
comments|6
attachments|1" "$(grep -E '^(users|roles|projects|columns|members|tasks|finished|overdue|comments|attachments)\|' "$scratch/counts-1")" || return 1
  is 'PORT|4|De făcut,În lucru,În review,Gata
TER|3|De făcut,În lucru,Gata' "$(in_db "select p.key, count(*), string_agg(c.name, ',' order by c.position)
    from projects p join board_columns c on c.project_id = p.id group by p.key order by p.key")" || return 1
  # Deadlines on all but one, and the admin told of six assignments and four comments.
  is 14 "$(in_db 'select count(*) from task_items where due_date is not null')" || return 1
  is 'CommentAdded|4
TaskAssigned|6' "$(in_db "select n.type, count(*) from notifications n join users u on u.id = n.user_id
    where u.email = '$admin_email' group by n.type order by n.type")"
}

demo_users_sign_in() {
  is 'ana.popescu@kanabol.local|False
dan.ionescu@kanabol.local|False
ioana.marin@kanabol.local|True' "$(in_db "select email, must_change_password from users where email like '%.%@kanabol.local' order by email")" || return 1
  for email in ana.popescu@kanabol.local dan.ionescu@kanabol.local; do
    printf '{"email":"%s","password":"%s"}' "$email" "$demo_password" >"$scratch/login.json"
    status=$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.83.0.2' \
      --data-binary "@$(native "$scratch/login.json")")
    status_is 200 "$status" || return 1
  done
}

second_run_makes_nothing() {
  run_demo_data "$scratch/demo-2.log" || return 1
  grep -q '^Nothing new' "$scratch/demo-2.log" || {
    cat "$scratch/demo-2.log"
    return 1
  }
  demo_counts >"$scratch/counts-2"
  diff "$scratch/counts-1" "$scratch/counts-2"
}

missing_piece_comes_back() {
  status=$(as "$admin_token" GET /api/tasks/by-key/PORT-1)
  status_is 200 "$status" || return 1
  file=$(sed -n 's/.*"attachments":\[{"id":"\([^"]*\)".*/\1/p' "$scratch/body")
  [ -n "$file" ] || return 1
  status=$(as "$admin_token" DELETE "/api/attachments/$file")
  status_is 204 "$status" || return 1
  run_demo_data "$scratch/demo-3.log" || return 1
  grep -q '^Done: 1 new\.$' "$scratch/demo-3.log" || {
    cat "$scratch/demo-3.log"
    return 1
  }
  demo_counts >"$scratch/counts-3"
  diff "$scratch/counts-1" "$scratch/counts-3"
}

docs_explain_the_demo_data() {
  file_has docs/deploy.md scripts/demo-data.sh KANABOL_ADMIN_PASSWORD KANABOL_DEMO_PASSWORD
}

task_4() {
  echo "19b.4 demo-data.sh"
  check "demo-data.sh is tracked as executable" tracked_and_executable scripts/demo-data.sh
  check "dash reads it" parses_as_posix_sh scripts/demo-data.sh
  check "docs/deploy.md tells how to run it" docs_explain_the_demo_data
  check "without its passwords it gives its usage, asking nothing" demo_data_needs_its_passwords
  check "the real API starts on a database of its own" api_is_up
  check "it waits for the admin's first password change, making nothing" demo_data_waits_for_the_first_admin
  check "the admin changes the first password" the_admin_changed_the_password
  check "demo-data.sh makes the data" first_run_makes_the_data
  check "3 users, 2 projects with columns, 15 tasks, comments, a file, notifications" data_is_what_was_asked
  check "Ana and Dan sign in with the demo password, Ioana has to change it" demo_users_sign_in
  check "run again, it makes nothing" second_run_makes_nothing
  check "a file deleted since comes back, alone" missing_piece_comes_back
  check "nothing it printed holds a password" no_password_in "$scratch/demo-1.log" "$scratch/demo-2.log" \
    "$scratch/demo-3.log" "$scratch/demo-first.log"
  stop_api
}

# ---- Task 5: a validator for every command with something to check (group 20; docs/SPEC.md §16).

# Every command of the Application project, found by reflection, with or without a FluentValidation validator
# in that project, and for those without one what their fields are: ids, the refresh token the cookie
# brings (a key looked up by its hash), a deadline, or anything else, which fails.
commands_of_the_application() {
  probe_dir="$scratch/commands"
  mkdir -p "$probe_dir"
  cat >"$probe_dir/commands.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(native "$PWD")/backend/src/Kanabol.Application/Kanabol.Application.csproj" />
  </ItemGroup>
</Project>
XML
  cat >"$probe_dir/Program.cs" <<'CS'
using System.Reflection;
using FluentValidation;
using Kanabol.Application.Abstractions.Messaging;

var application = typeof(ICommand<>).Assembly;
var types = application.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false }).ToList();
var validated = types
    .SelectMany(type => type.GetInterfaces())
    .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
    .Select(contract => contract.GetGenericArguments()[0])
    .ToHashSet();
var commands = types
    .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>)))
    .OrderBy(type => type.Name, StringComparer.Ordinal);

foreach (var command in commands)
{
    if (validated.Contains(command))
    {
        Console.WriteLine($"validated {command.Name}");
        continue;
    }

    var fields = command.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(field => (field.PropertyType == typeof(Guid) || field.PropertyType == typeof(Guid?)) && field.Name.EndsWith("Id", StringComparison.Ordinal) ? $"{field.Name}:id"
            : field.PropertyType == typeof(string) && field.Name == "RefreshToken" ? $"{field.Name}:token"
            : field.PropertyType == typeof(DateTimeOffset?) && field.Name == "DueDate" ? $"{field.Name}:deadline"
            : $"{field.Name}:OTHER {field.PropertyType.Name}")
        .ToList();
    Console.WriteLine($"unvalidated {command.Name} {(fields.Count == 0 ? "nothing" : string.Join(' ', fields))}");
}
CS
  dotnet build "$probe_dir/commands.csproj" --nologo -v q >"$probe_dir/build.log" 2>&1 || {
    tail -n 20 "$probe_dir/build.log"
    return 1
  }
  dotnet "$probe_dir/bin/Debug/net10.0/commands.dll" | tr -d '\r' >"$scratch/commands.txt" || return 1
  cat "$scratch/commands.txt"
}

every_command_is_found() {
  commands_of_the_application >/dev/null || return 1
  # As many as there are record ...Command( in the code, so the reflection can't have missed one.
  in_code=$(grep -rhoE 'record [A-Za-z]+Command\b' backend/src/Kanabol.Application --include='*Command.cs' | sort -u | wc -l)
  found=$(wc -l <"$scratch/commands.txt")
  is "$in_code" "$found" || return 1
  [ "$found" -ge 40 ] || {
    echo "only $found commands"
    return 1
  }
}

commands_with_fields_have_a_validator() {
  [ -s "$scratch/commands.txt" ] || commands_of_the_application >/dev/null || return 1
  ! grep 'OTHER' "$scratch/commands.txt" || return 1
  # Those the tests know take text, numbers or lists, each through its validator.
  for command in CreateTaskCommand RenameTaskCommand AssignTaskCommand MoveTaskCommand CreateUserCommand CreateRoleCommand \
    AddCommentCommand AddAttachmentCommand LoginUserCommand ChangePasswordCommand RecordClientLogsCommand MoveBoardColumnCommand; do
    grep -qxF "validated $command" "$scratch/commands.txt" || {
      echo "$command has no validator"
      return 1
    }
  done
  echo "$(grep -c '^validated ' "$scratch/commands.txt") with a validator, $(grep -c '^unvalidated ' "$scratch/commands.txt") without:"
  grep '^unvalidated ' "$scratch/commands.txt"
}

task_5() {
  echo "20.5 a validator for every command with more than ids"
  check "reflection finds every command of the Application project" every_command_is_found
  check "every command with more than ids, nothing or a deadline has a FluentValidation validator" commands_with_fields_have_a_validator
}

tasks=${*:-1 2 3 4 5}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
    2) task_2 ;;
    3) task_3 ;;
    4) task_4 ;;
    5) task_5 ;;
    *)
      echo "No task $task."
      failures=$((failures + 1))
      ;;
  esac
done

if [ "$failures" -eq 0 ]; then
  echo "All checks passed."
else
  echo "$failures check(s) failed."
  exit 1
fi
