#!/bin/sh
# Fills a running Kanabol with the tasks for trying the search by meaning (docs/SPEC.md section 19.6), through
# its API. It runs scripts/demo-data.sh first, whose colleagues the tasks go to, then adds the projects of five
# teams of one company and its old site, archived, with the 262 tasks of scripts/search-data.txt: in Romanian,
# written as people write them, some with a title only, some without diacritics, 8 of them archived. For a
# machine of one's own or a trial server, not one with the team's real work: the projects' keys are FAC, MAG,
# RU, INF, SUP and SITE.
#
# Usage: KANABOL_ADMIN_PASSWORD=... KANABOL_DEMO_PASSWORD=... scripts/search-data.sh [url]
#   the same as scripts/demo-data.sh, whose header says the rest
#
# It never calls the model: the vectors come from the vectorization worker, at most Embeddings:Interval after,
# when Embeddings:Enabled is true. Running it again makes nothing twice: a project is found by its key, and its
# tasks by their numbers, which their order in the file gives. It never asks anything, and prints no password.

set -u

here=$(dirname "$0")
data="$here/search-data.txt"
demo_password=${KANABOL_DEMO_PASSWORD:-}

if [ -z "${KANABOL_ADMIN_PASSWORD:-}" ] || [ "${#demo_password}" -lt 10 ]; then
  echo "Usage: KANABOL_ADMIN_PASSWORD=... KANABOL_DEMO_PASSWORD=... scripts/search-data.sh [url]" >&2
  echo "The same as scripts/demo-data.sh, which it runs first: its header says the rest." >&2
  exit 2
fi

# The colleagues, PORT and TER.
sh "$here/demo-data.sh" "$@" || exit

url=${1:-http://localhost:4200}
url=${url%/}
admin_email=${KANABOL_ADMIN_EMAIL:-admin@kanabol.local}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
body="$work/body"
created=0

curl_options=
if [ -n "${KANABOL_CACERT:-}" ]; then
  curl_options="--cacert $KANABOL_CACERT"
fi

# ---- Requests, as in demo-data.sh.

native() { # As Windows curl reads a path; the same path elsewhere.
  cygpath -m "$1" 2>/dev/null || printf '%s' "$1"
}

json_text() { # json_text <text>: the text as a JSON string, quotes included, each \n in it a new line
  printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/\\\\n/\\n/g')"
}

say() {
  printf '%s\n' "$*"
}

stop() {
  echo "search-data.sh: $1" >&2
  exit 1
}

call() { # call <method> <path> [curl arguments...]: the answer in $body, the status printed
  method=$1
  path=$2
  shift 2
  rm -f "$body"
  # shellcheck disable=SC2086 # the options are meant to split
  curl -sS $curl_options --max-time 60 -o "$(native "$body")" -D "$(native "$work/headers")" -w '%{http_code}' \
    -X "$method" "$@" "$url$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    # From a file: Windows hands curl its arguments in the ANSI code page, where ă turns into a.
    printf '%s' "$4" >"$work/request.json"
    call "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
      --data-binary "@$(native "$work/request.json")"
  else
    call "$method" "$path" -H "Authorization: Bearer $token"
  fi
}

expect() { # expect <status...> -- <what was asked>: stops unless the last status is one of them
  wanted=
  while [ "$1" != -- ]; do
    wanted="$wanted $1"
    shift
  done
  shift
  case " $wanted " in
    *" $status "*) ;;
    *) stop "$1 answered $status: $(head -c 400 "$body" 2>/dev/null)" ;;
  esac
}

field() { # field <name>: the first text value of that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$body" | head -n 1
}

nl='
'
id_of_item() { # id_of_item <pattern> [file]: in a list, the id of the first object whose own fields match it
  sed "s/{\"id\":/\\$nl{\"id\":/g" "${2:-$body}" | grep -E -- "$1" | head -n 1 | sed -n 's/^{"id":"\([^"]*\)".*/\1/p'
}

login() { # login <email> <password>: the status printed, the session in $body; waits out the login limit
  printf '{"email":%s,"password":%s}' "$(json_text "$1")" "$(json_text "$2")" >"$work/login.json"
  for attempt in 1 2 3 4; do
    status=$(call POST /api/auth/login -H 'Content-Type: application/json' --data-binary "@$(native "$work/login.json")")
    [ "$status" = 429 ] || break
    wait=$(sed -n 's/^[Rr]etry-[Aa]fter: *\([0-9]*\).*/\1/p' "$work/headers" | head -n 1)
    say "  (the API allows 5 logins a minute: waiting ${wait:-60} s)" >&2
    sleep "${wait:-60}"
  done
  printf '%s' "$status"
}

# ---- The admin and the colleagues demo-data.sh made.

say "Search data at $url"
status=$(login "$admin_email" "$KANABOL_ADMIN_PASSWORD")
expect 200 -- "the admin's login"
admin=$(field accessToken)
status=$(as "$admin" GET /api/auth/me)
expect 200 -- "who the admin is"
admin_id=$(field id)

user_id_of() { # user_id_of <email>: its id in user_id
  user_id=
  page=1
  while [ -z "$user_id" ]; do
    status=$(as "$admin" GET "/api/users?page=$page&pageSize=100")
    expect 200 -- "the users"
    user_id=$(id_of_item "\"email\":$(json_text "$1")")
    grep -q '"id":' "$body" || break
    page=$((page + 1))
  done
  [ -n "$user_id" ] || stop "no user $1, which demo-data.sh makes"
}

user_id_of ana.popescu@kanabol.local
ana_id=$user_id
user_id_of dan.ionescu@kanabol.local
dan_id=$user_id
user_id_of ioana.marin@kanabol.local
ioana_id=$user_id

assignee_ids() { # assignee_ids <names separated by commas, or ->: their ids, as a JSON list's items, in ids
  ids=
  [ "$1" = - ] && return 0
  # who, not name: a function's variables are global in sh, and project() keeps its name in name.
  for who in $(printf '%s' "$1" | tr ',' ' '); do
    case $who in
      admin) id=$admin_id ;;
      ana) id=$ana_id ;;
      dan) id=$dan_id ;;
      ioana) id=$ioana_id ;;
      *) stop "$data: no assignee named $who" ;;
    esac
    ids="$ids${ids:+,}\"$id\""
  done
}

# ---- Projects and their tasks.

status=$(as "$admin" GET '/api/projects?includeArchived=true&pageSize=100')
expect 200 -- "the projects"
cp "$body" "$work/all-projects"
status=$(as "$admin" GET '/api/projects?pageSize=100')
expect 200 -- "the projects not archived"
cp "$body" "$work/open-projects"

ensure_member() { # ensure_member <project id> <user id>: a Member, unless they're in it already
  status=$(as "$admin" GET "/api/projects/$1/members")
  expect 200 -- "the members"
  grep -q "\"userId\":\"$2\"" "$body" && return 0
  status=$(as "$admin" POST "/api/projects/$1/members" "{\"userId\":\"$2\",\"role\":\"Member\"}")
  expect 201 200 -- "a member"
  created=$((created + 1))
}

archive_task() { # archive_task <task id>
  status=$(as "$admin" DELETE "/api/tasks/$1")
  expect 204 200 -- "archiving a task"
  created=$((created + 1))
}

# project <key> <name> <description> [archived]: the project, its members and its tasks from the file. An
# archived one is archived once its tasks are made, so finding it archived means it's complete.
project() {
  key=$1
  name=$2
  new_tasks=0
  project_id=$(id_of_item "\"key\":\"$key\"" "$work/all-projects")
  if [ -n "$project_id" ] && ! sed "s/{\"id\":/\\$nl{\"id\":/g" "$work/all-projects" | grep -F "\"key\":\"$key\"" |
    grep -qF "\"name\":$(json_text "$name")"; then
    stop "the project $key isn't $name: this script adds nothing to a project of someone else's"
  fi
  if [ -n "$project_id" ] && [ -z "$(id_of_item "\"key\":\"$key\"" "$work/open-projects")" ]; then
    say "  $key $name: archived, already there"
    return 0
  fi
  if [ -z "$project_id" ]; then
    status=$(as "$admin" POST /api/projects "{\"name\":$(json_text "$name"),\"key\":\"$key\",\"description\":$(json_text "$3")}")
    expect 201 -- "the project $key"
    project_id=$(field id)
    created=$((created + 1))
  fi
  for member in "$ana_id" "$dan_id" "$ioana_id"; do
    ensure_member "$project_id" "$member"
  done

  # Tasks are never deleted, only archived, so a project with n tasks has the first n of the file.
  status=$(as "$admin" GET "/api/projects/$project_id/tasks?includeArchived=true&pageSize=1")
  expect 200 -- "the tasks of $key"
  there=$(sed -n 's/.*"totalCount":\([0-9]*\).*/\1/p' "$body")
  grep "^$key|" "$data" >"$work/lines"
  number=0
  while IFS='|' read -r _ priority assignees archived title description <&3; do
    number=$((number + 1))
    if [ "$number" -le "${there:-0}" ]; then
      [ "$archived" = archived ] || continue
      # Made by a run stopped before it was archived.
      status=$(as "$admin" GET "/api/tasks/by-key/$key-$number?includeArchived=true")
      expect 200 -- "the task $key-$number"
      if grep -q '"archivedAt":null' "$body"; then
        archive_task "$(field id)"
        say "    $key-$number archived, as in the file"
      fi
      continue
    fi
    assignee_ids "$assignees"
    text=null
    [ -z "$description" ] || text=$(json_text "$description")
    status=$(as "$admin" POST "/api/projects/$project_id/tasks" \
      "{\"title\":$(json_text "$title"),\"description\":$text,\"priority\":\"$priority\",\"assigneeIds\":[$ids]}")
    expect 201 -- "the task $key-$number"
    [ "$(field key)" = "$key-$number" ] ||
      stop "the new task of $key is $(field key), not $key-$number: the project has tasks this script didn't make"
    created=$((created + 1))
    new_tasks=$((new_tasks + 1))
    [ "$archived" = archived ] && archive_task "$(field id)"
  done 3<"$work/lines"

  if [ "$new_tasks" -eq 0 ]; then
    say "  $key $name: $number tasks, already there"
  else
    say "  $key $name: $number tasks, $new_tasks of them new"
  fi
  if [ "${4:-}" = archived ]; then
    status=$(as "$admin" DELETE "/api/projects/$project_id")
    expect 204 200 -- "archiving the project $key"
    created=$((created + 1))
    say "    $key archived, as in the file"
  fi
}

say "Projects"
project FAC "Facturare" "Emiterea facturilor, încasările și legătura cu contabilitatea."
project MAG "Magazin online" "Magazinul online: catalogul, coșul, plata și livrarea."
project RU "Resurse umane" "Intranetul angajaților: concedii, pontaj, angajări și cursuri."
project INF "Infrastructură" "Serverele, rețeaua, copiile de siguranță și securitatea firmei."
project SUP "Suport clienți" "Tichetele, telefoanele și răspunsurile pentru clienți."
project SITE "Site de prezentare" "Site-ul vechi al firmei, înlocuit de magazinul online." archived

if [ "$created" -eq 0 ]; then
  say "Nothing new: the search data was already there."
else
  say "Done: $created new."
fi
say "The vectors come from the vectorization worker, when Embeddings:Enabled is true."
