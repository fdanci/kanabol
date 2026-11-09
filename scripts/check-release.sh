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
#
# Docker builds the images and runs the stacks; everything is removed at the end.

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
  for project in $compose_projects; do
    (cd "$probe" 2>/dev/null && probe_compose "$project" down -v --remove-orphans >/dev/null 2>&1)
  done
  images=$(docker images -q --filter "reference=kanabol-*:$check_tag")
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
  seq_hash=$(echo "$seq_password" | docker run --rm -i datalust/seq config hash | tr -d '\r')
  [ -n "$seq_hash" ] || return 1
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

tasks=${*:-1}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
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
