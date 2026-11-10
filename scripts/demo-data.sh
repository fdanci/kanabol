#!/bin/sh
# Fills a running Kanabol with demonstration data, through its API (docs/SPEC.md section 13.5): three users,
# two projects with their columns, 15 tasks with deadlines (some overdue, some finished on time and some
# late), comments, a file, and the notifications those make for the admin. For screenshots and demos, not
# for a server with real work: the projects' keys are PORT and TER, and the users are @kanabol.local.
#
# Usage: KANABOL_ADMIN_PASSWORD=... KANABOL_DEMO_PASSWORD=... scripts/demo-data.sh [url]
#   url                     the app's address, http://localhost:4200 unless given (the API on its own works too)
#   KANABOL_ADMIN_EMAIL     admin@kanabol.local unless set; an account whose password was already changed
#   KANABOL_ADMIN_PASSWORD  its password
#   KANABOL_DEMO_PASSWORD   the password of Ana Popescu and Dan Ionescu, at least 10 characters. Ioana Marin
#                           gets it as an initial password, to change at her first login
#   KANABOL_CACERT          a CA file for curl, such as certs/ca.crt for the server's own certificate
#
# Running it again makes nothing twice: it finds what's there by the users' emails, the roles' names, the
# projects' keys and the tasks' keys, and adds only what's missing. It never asks anything, and prints no
# password. Logins over the API's limit of 5 a minute wait for as long as the API says.

set -u

url=${1:-http://localhost:4200}
url=${url%/}
admin_email=${KANABOL_ADMIN_EMAIL:-admin@kanabol.local}
admin_password=${KANABOL_ADMIN_PASSWORD:-}
demo_password=${KANABOL_DEMO_PASSWORD:-}

if [ -z "$admin_password" ] || [ "${#demo_password}" -lt 10 ]; then
  echo "Usage: KANABOL_ADMIN_PASSWORD=... KANABOL_DEMO_PASSWORD=... scripts/demo-data.sh [url]" >&2
  echo "KANABOL_DEMO_PASSWORD needs at least 10 characters. The header of this script says the rest." >&2
  exit 2
fi
# Ana's and Dan's first password, which the script changes to KANABOL_DEMO_PASSWORD at once.
first_password="$demo_password (prima)"

new_users=
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
body="$work/body"
created=

curl_options=
if [ -n "${KANABOL_CACERT:-}" ]; then
  curl_options="--cacert $KANABOL_CACERT"
fi

native() { # As Windows curl reads a path; the same path elsewhere.
  cygpath -m "$1" 2>/dev/null || printf '%s' "$1"
}

json_text() { # json_text <text>: the text as a JSON string, quotes included, its lines joined by \n
  printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | awk 'NR > 1 { printf "\\n" } { printf "%s", $0 }')"
}

say() {
  printf '%s\n' "$*"
}

made() { # made <what>: counts what this run created
  created="$created
$1"
  say "  + $1"
}

stop() {
  echo "demo-data.sh: $1" >&2
  exit 1
}

# ---- Requests.

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
id_of_item() { # id_of_item <pattern>: in a list, the id of the first object whose own fields match it
  sed "s/{\"id\":/\\$nl{\"id\":/g" "$body" | grep -E -- "$1" | head -n 1 | sed -n 's/^{"id":"\([^"]*\)".*/\1/p'
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

change_password() { # change_password <token> <current> <new>: the status printed, the new session in $body
  as "$1" POST /api/auth/change-password "{\"currentPassword\":$(json_text "$2"),\"newPassword\":$(json_text "$3")}"
}

# ---- The admin.

say "Kanabol at $url"
status=$(login "$admin_email" "$admin_password")
[ "$status" != 000 ] || stop "nothing answers at $url"
expect 200 -- "the admin's login"
grep -q '"mustChangePassword":true' "$body" &&
  stop "$admin_email still has to change their password: sign in once in the browser first"
admin=$(field accessToken)
status=$(as "$admin" GET /api/auth/me)
expect 200 -- "who the admin is"
admin_id=$(field id)

# ---- Roles and users.

ensure_role() { # ensure_role <name> <permissions as a JSON list>: its id in role_id
  status=$(as "$admin" GET '/api/roles?pageSize=100')
  expect 200 -- "the roles"
  role_id=$(id_of_item "\"name\":$(json_text "$1")")
  [ -n "$role_id" ] && return 0
  status=$(as "$admin" POST /api/roles "{\"name\":$(json_text "$1"),\"permissions\":$2}")
  expect 201 -- "the role $1"
  role_id=$(field id)
  made "role $1"
}

find_user() { # find_user <email>: its id in user_id, or nothing
  user_id=
  page=1
  while [ -z "$user_id" ]; do
    status=$(as "$admin" GET "/api/users?page=$page&pageSize=100")
    expect 200 -- "the users"
    user_id=$(id_of_item "\"email\":$(json_text "$1")")
    grep -q '"id":' "$body" || break
    page=$((page + 1))
  done
}

ensure_user() { # ensure_user <email> <name> <role id> <initial password>: its id in user_id
  find_user "$1"
  [ -n "$user_id" ] && return 0
  status=$(as "$admin" POST /api/users \
    "{\"email\":$(json_text "$1"),\"displayName\":$(json_text "$2"),\"roleId\":\"$3\",\"initialPassword\":$(json_text "$4")}")
  expect 201 -- "the user $1"
  user_id=$(field id)
  new_users="$new_users $1"
  made "user $2 <$1>"
}

session_of() { # session_of <email>: a token of that demo user, who can work, in session
  case " $new_users " in
    *" $1 "*) ;;
    *)
      status=$(login "$1" "$demo_password")
      if [ "$status" = 200 ]; then
        session=$(field accessToken)
        grep -q '"mustChangePassword":true' "$body" || return 0
      fi
      ;;
  esac
  # Not changed yet: from the first password, which the script gave, to the demo one.
  status=$(login "$1" "$first_password")
  expect 200 -- "the first login of $1"
  status=$(change_password "$(field accessToken)" "$first_password" "$demo_password")
  expect 200 -- "the new password of $1"
  session=$(field accessToken)
}

say "Users and roles"
ensure_role "Manager de proiect" '["projects.create","tasks.create","tasks.archive","reports.view"]'
manager_role=$role_id
ensure_role "Membru" '["tasks.create"]'
member_role=$role_id
ensure_user ana.popescu@kanabol.local "Ana Popescu" "$manager_role" "$first_password"
ana_id=$user_id
ensure_user dan.ionescu@kanabol.local "Dan Ionescu" "$member_role" "$first_password"
dan_id=$user_id
ensure_user ioana.marin@kanabol.local "Ioana Marin" "$member_role" "$demo_password"
ioana_id=$user_id

ana=
dan=
ana_session() {
  [ -n "$ana" ] && return 0
  session_of ana.popescu@kanabol.local
  ana=$session
}
dan_session() {
  [ -n "$dan" ] && return 0
  session_of dan.ionescu@kanabol.local
  dan=$session
}

# ---- Projects, columns and members.

ensure_project() { # ensure_project <token of its creator> <key> <name> <description>: its id in project_id
  status=$(as "$admin" GET '/api/projects?includeArchived=true&pageSize=100')
  expect 200 -- "the projects"
  project_id=$(id_of_item "\"key\":\"$2\"")
  [ -n "$project_id" ] && return 0
  status=$(as "$1" POST /api/projects "{\"name\":$(json_text "$3"),\"key\":\"$2\",\"description\":$(json_text "$4")}")
  expect 201 -- "the project $2"
  project_id=$(field id)
  made "project $2 $3"
}

column_at() { # column_at <project id> <position>: that column's id
  status=$(as "$admin" GET "/api/projects/$1")
  expect 200 -- "the project's columns"
  id_of_item "\"position\":$2[,}]"
}

done_column() { # done_column <project id>: the id of Gata, its Done column
  status=$(as "$admin" GET "/api/projects/$1")
  expect 200 -- "the project's columns"
  id_of_item '"kind":"Done"'
}

ensure_member() { # ensure_member <project id> <user id> <Owner|Member>
  status=$(as "$admin" GET "/api/projects/$1/members")
  expect 200 -- "the members"
  grep -q "\"userId\":\"$2\"" "$body" && return 0
  status=$(as "$admin" POST "/api/projects/$1/members" "{\"userId\":\"$2\",\"role\":\"$3\"}")
  expect 201 200 -- "a member"
  made "member $2 in $1 as $3"
}

ensure_review_column() { # ensure_review_column <project id>: "În review" of its own, right before Gata
  status=$(as "$admin" GET "/api/projects/$1")
  expect 200 -- "the project's columns"
  grep -q '"kind":"Custom"' "$body" && return 0
  status=$(as "$admin" POST "/api/projects/$1/board-columns" '{"name":"În review","isDone":false}')
  expect 201 -- "the review column"
  column=$(field id)
  status=$(as "$admin" PATCH "/api/projects/$1/board-columns/$column/position" '{"position":3}')
  expect 200 -- "the review column's place"
  made "column În review in $1"
}

say "Projects"
ensure_project "$admin" PORT "Portal clienți" "Portalul prin care clienții își văd comenzile, facturile și livrările."
portal=$project_id
ensure_review_column "$portal"
ensure_member "$portal" "$ana_id" Owner
ensure_member "$portal" "$dan_id" Member
ensure_member "$portal" "$ioana_id" Member

status=$(as "$admin" GET '/api/projects?includeArchived=true&pageSize=100')
expect 200 -- "the projects"
if [ -z "$(id_of_item '"key":"TER"')" ]; then
  ana_session
fi
ensure_project "${ana:-$admin}" TER "Aplicația de teren" "Aplicația mobilă a echipelor de intervenție: rapoarte, fotografii și semnătura clientului."
field_app=$project_id
ensure_member "$field_app" "$admin_id" Member
ensure_member "$field_app" "$dan_id" Member
ensure_member "$field_app" "$ioana_id" Member

# ---- Tasks.

deadline() { # deadline <days from today>: the end of that day, in this machine's time
  at=$(($(date +%s) + $1 * 86400))
  printf '%sT23:59:59%s' "$(date -d "@$at" +%Y-%m-%d)" \
    "$(date -d "@$at" +%z | sed 's/^\([+-][0-9][0-9]\)\([0-9][0-9]\)$/\1:\2/')"
}

# task <key> <creator: admin|ana> <project id> <column position> <priority> <days to the deadline, or -> <done: yes|no>
#      <assignee ids, separated by commas> <title> <description>
task() {
  key=$1
  creator=$2
  project=$3
  position=$4
  priority=$5
  days=$6
  finished=$7
  assignees=$8
  title=$9
  shift 9
  description=$1
  status=$(as "$admin" GET "/api/tasks/by-key/$key?includeArchived=true")
  if [ "$status" = 200 ]; then
    task_id=$(field id)
  else
    expect 404 -- "the task $key"
    if [ "$creator" = ana ]; then
      ana_session
      creator_token=$ana
    else
      creator_token=$admin
    fi
    column=$(column_at "$project" "$position")
    due=null
    [ "$days" = - ] || due="\"$(deadline "$days")\""
    ids=$(printf '%s' "$assignees" | sed -e 's/\([^,][^,]*\)/"\1"/g')
    status=$(as "$creator_token" POST "/api/projects/$project/tasks" \
      "{\"title\":$(json_text "$title"),\"description\":$(json_text "$description"),\"priority\":\"$priority\",\"columnId\":\"$column\",\"assigneeIds\":[$ids],\"dueDate\":$due}")
    expect 201 -- "the task $key"
    task_id=$(field id)
    made "task $(field key) $title"
  fi
  if [ "$finished" = yes ] && grep -q '"completedAt":null' "$body"; then
    status=$(as "$admin" PATCH "/api/tasks/$task_id/position" "{\"columnId\":\"$(done_column "$project")\",\"position\":null}")
    expect 200 -- "finishing $key"
    made "$key finished"
  fi
}

comments() { # comments <task id> [<author: admin|ana|dan> <text>]...: written only on a task that has none
  task_id=$1
  shift
  status=$(as "$admin" GET "/api/tasks/$task_id/comments")
  expect 200 -- "the comments"
  [ "$(tr -d ' \r\n' <"$body")" = '[]' ] || return 0
  while [ "$#" -ge 2 ]; do
    case "$1" in
      ana) ana_session && author=$ana ;;
      dan) dan_session && author=$dan ;;
      *) author=$admin ;;
    esac
    status=$(as "$author" POST "/api/tasks/$task_id/comments" "{\"body\":$(json_text "$2")}")
    expect 201 -- "a comment"
    made "comment by $1"
    shift 2
  done
}

say "Tasks"
me=$admin_id
task PORT-1 ana "$portal" 1 High 5 no "$me,$dan_id" "Autentificare cu SSO pentru clienți" "Clienții mari vor să intre cu contul companiei.

- OIDC cu furnizorul fiecărui client
- maparea rolurilor la primul login
- **fără** parole locale pentru ei"
sso=$task_id
task PORT-2 ana "$portal" 0 Medium 12 no "$dan_id" "Pagina de facturi: filtre după lună" "Filtre după lună și an, cu totalul perioadei sub tabel."
task PORT-3 ana "$portal" 3 High -2 no "$ioana_id" "Export PDF al facturii" "PDF-ul pleacă pe email și se descarcă din pagină."
pdf=$task_id
task PORT-4 admin "$portal" 0 Low - no "" "Notificări pe email pentru comenzile noi" "Un email scurt, cu link spre comandă."
email=$task_id
task PORT-5 ana "$portal" 0 Low -6 yes "$ioana_id" "Corectură pe textele paginii de start" "Diacriticele și titlurile secțiunilor."
task PORT-6 ana "$portal" 1 Critical -1 no "$me" "Migrare la noul server de baze de date" "Fereastra de mentenanță e sâmbătă, între 22:00 și 02:00."
migration=$task_id
task PORT-7 ana "$portal" 1 Medium 3 yes "$me,$dan_id" "Formular de contact cu protecție anti-spam" "Fără captcha vizibil: un câmp ascuns și o limită pe adresă."
task PORT-8 admin "$portal" 0 Medium 20 no "$dan_id,$ioana_id" "Istoricul comenzilor: paginare" "Câte 25 pe pagină, cu căutare după număr."
task PORT-9 ana "$portal" 0 High 8 no "$me" "Audit de accesibilitate" "Contrast, ordinea focusului și etichetele formularelor."
task TER-1 ana "$field_app" 1 Critical 4 no "$dan_id" "Sincronizare offline a intervențiilor" "Datele se păstrează pe telefon și pleacă la prima conexiune."
task TER-2 ana "$field_app" 0 High -3 no "$me,$dan_id" "Fotografii atașate la raport" "Până la 10 fotografii pe raport, micșorate pe telefon."
photos=$task_id
task TER-3 ana "$field_app" 0 Medium 10 yes "$ana_id" "Semnătura clientului pe ecran" "Semnătura intră în PDF-ul raportului."
task TER-4 ana "$field_app" 0 Medium 15 no "$ioana_id" "Hartă cu intervențiile zilei" "Pinii în ordinea programării, cu adresa la atingere."
task TER-5 ana "$field_app" 1 Low -4 yes "$dan_id" "Testare pe Android 12" "Telefoanele echipelor din Cluj și Iași."
task TER-6 ana "$field_app" 1 High 2 no "$me" "Login cu PIN" "PIN de 6 cifre după primul login cu parola."

say "Comments"
comments "$sso" dan "Am configurat clientul OIDC pe mediul de test. Mai rămâne maparea rolurilor." \
  admin "Perfect, mă uit mâine pe maparea rolurilor."
comments "$migration" dan "Backup-ul de pe serverul vechi e făcut. Mutăm sâmbătă cum am stabilit?"
comments "$pdf" ana "Clientul vrea și logo-ul pe fiecare pagină a PDF-ului."
comments "$photos" dan "Pe telefoanele vechi fotografiile ies de 8 MB. Le micșorăm înainte de upload?"
comments "$email" dan "Putem folosi coada de mesaje pe care o avem deja pentru facturi."

say "Files"
status=$(as "$admin" GET "/api/tasks/$sso")
expect 200 -- "PORT-1"
if grep -q '"attachments":\[\]' "$body"; then
  cat >"$work/specificatie-sso.md" <<'EOF'
# Autentificare cu SSO

1. Clientul își configurează furnizorul (Entra ID, Google Workspace sau Keycloak).
2. Kanabol primește `client_id` și adresa furnizorului.
3. La primul login, rolul vine din grupul utilizatorului.
EOF
  status=$(call POST "/api/tasks/$sso/attachments" -H "Authorization: Bearer $admin" \
    -F "file=@$(native "$work/specificatie-sso.md")")
  expect 201 -- "the file"
  made "file specificatie-sso.md"
fi

count=$(printf '%s' "$created" | grep -c .)
if [ "$count" -eq 0 ]; then
  say "Nothing new: the demonstration data was already there."
else
  say "Done: $count new."
fi
say "Ana Popescu and Dan Ionescu sign in with KANABOL_DEMO_PASSWORD; Ioana Marin with it too, and changes it."
