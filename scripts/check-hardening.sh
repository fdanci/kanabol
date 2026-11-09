#!/bin/sh
# Acceptance checks for the hardening (group 19 in the project's Tasks.md; docs/SPEC.md §4.5, §6.2, §8.1, §9,
# §13.2, §14.6, §14.7, §16; docs/UI_UX_SPEC.md §2, §7.8). The tests and specs hold the rules, so these checks
# run them, and add what they can't see: nginx as the image runs it, the real API process on a real
# PostgreSQL, the frontend's production build and the merge into main.
#
# Usage: scripts/check-hardening.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  nginx's headers: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy and the
#      CSP of docs/SPEC.md §13.2, at server level in nginx-app.conf, on the image built from frontend/: the
#      app, a deep link, a script, the manifest, what the API answers and nginx's own 413, each once; HSTS
#      only in nginx.prod.conf, on 443, a year without preload, and not on port 80's redirect, which still
#      goes to HTTPS; the built index.html with nothing the CSP would block (no inline script or handler,
#      no stylesheet left waiting on media="print", nothing from another origin), with controls: a header
#      in a location, a header without always, the build's inline script. Docker.
#
# Docker runs nginx and a stand-in for the API.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

# Shells opened before Docker Desktop was installed don't have it on the PATH.
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
# Git Bash would otherwise rewrite container paths like /etc/nginx into Windows paths.
docker() {
  MSYS_NO_PATHCONV=1 command docker "$@"
}

failures=0
scratch=$(mktemp -d)
run_id="kanabol-hardening-$$"
network="$run_id-net"
web_image="kanabol-hardening-web:$$"
web_url=

# docs/SPEC.md §13.2, written here as the spec has them, not read from the configuration.
csp="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'"
hsts='max-age=31536000'

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

native() { # native <path>: as Windows programs (node, docker, dotnet) read it
  cygpath -m "$1" 2>/dev/null || echo "$1"
}

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
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

cleanup() {
  containers=$(docker ps -aq --filter "label=$run_id" 2>/dev/null)
  [ -z "$containers" ] || docker rm -f -v $containers >/dev/null 2>&1
  docker network rm "$network" >/dev/null 2>&1
  docker rmi -f "$web_image" >/dev/null 2>&1
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---- 19.1: nginx's headers.

# Every add_header at server level, outside a location, so a location never loses the server's
# (docs/SPEC.md §13.2). nginx-app.conf is included inside a server, so its server level is the file's own.
headers_at_server_level() { # headers_at_server_level <configuration file> <depth of the server level>
  awk -v level="$2" '
    { line = $0; sub(/#.*/, "", line) }
    line ~ /add_header/ && depth != level { print FILENAME ":" NR ": " $0; bad = 1 }
    { depth += gsub(/\{/, "{", line) - gsub(/\}/, "}", line) }
    END { exit bad }
  ' "$1"
}

app_conf_has_the_headers() {
  for header in X-Content-Type-Options X-Frame-Options Referrer-Policy Permissions-Policy Content-Security-Policy; do
    grep -q "^add_header $header .* always;\$" frontend/nginx-app.conf || {
      echo "no add_header $header ... always; in nginx-app.conf"
      return 1
    }
  done
}

hsts_only_in_the_443_server() {
  if grep -n 'Strict-Transport-Security' frontend/nginx-app.conf frontend/nginx.conf; then
    return 1
  fi
  # The server block that holds HSTS listens on 443, and the one on 80 has none.
  awk '
    /^server/ { block++ }
    /listen 443/ { tls[block] = 1 }
    /Strict-Transport-Security/ { hsts[block] = 1 }
    END {
      for (b in hsts) if (!tls[b]) { print "HSTS in a server without 443"; exit 1 }
      for (b in tls) if (!hsts[b]) { print "the 443 server has no HSTS"; exit 1 }
    }
  ' frontend/nginx.prod.conf
}

on_network() {
  docker network inspect "$network" >/dev/null 2>&1 || docker network create --label "$run_id" "$network" >/dev/null
}

# A stand-in for the API, under the name nginx looks for: it answers JSON to anything.
start_api_stub() {
  on_network || return 1
  docker ps -q --filter "name=^$run_id-stub$" | grep -q . && return 0
  docker run -d --label "$run_id" --name "$run_id-stub" --network "$network" --network-alias api node:22-alpine node -e '
    require("http").createServer((request, response) => {
      request.resume();
      request.on("end", () => {
        response.setHeader("Content-Type", "application/json");
        response.end(JSON.stringify({ url: request.url }));
      });
    }).listen(8080);
  ' >/dev/null
}

build_web_image() {
  docker build -t "$web_image" frontend >"$scratch/web-build.log" 2>&1 || {
    tail -n 25 "$scratch/web-build.log"
    return 1
  }
}

# nginx with the image's own configuration, or with another shared part mounted over it. Sets web_url.
start_web() { # start_web <name> [nginx-app.conf to use]
  start_api_stub || return 1
  mount=
  [ -z "${2:-}" ] || mount="-v $(native "$2"):/etc/nginx/kanabol-app.conf:ro"
  # shellcheck disable=SC2086 # the mount is meant to split
  docker run -d --label "$run_id" --name "$run_id-$1" --network "$network" -p 127.0.0.1::80 $mount "$web_image" >/dev/null || return 1
  web_url="http://$(docker port "$run_id-$1" 80/tcp | head -n 1)"
  curl -sS -o /dev/null --retry 15 --retry-connrefused --retry-delay 1 "$web_url/" || {
    docker logs "$run_id-$1" 2>&1 | tail -n 10
    return 1
  }
}

# The production server: nginx.prod.conf over nginx.conf, with a throwaway certificate. Sets http_port and
# https_port.
start_prod_web() {
  start_api_stub || return 1
  mkdir -p "$scratch/certs"
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=kanabol.example' \
    -keyout "$(native "$scratch/certs/server.key")" -out "$(native "$scratch/certs/server.crt")" >/dev/null 2>&1 || return 1
  docker run -d --label "$run_id" --name "$run_id-prod" --network "$network" -p 127.0.0.1::80 -p 127.0.0.1::443 \
    -v "$(native "$PWD/frontend/nginx.prod.conf"):/etc/nginx/conf.d/default.conf:ro" \
    -v "$(native "$scratch/certs"):/etc/nginx/certs:ro" "$web_image" >/dev/null || return 1
  http_port=$(docker port "$run_id-prod" 80/tcp | head -n 1 | sed 's/.*://')
  https_port=$(docker port "$run_id-prod" 443/tcp | head -n 1 | sed 's/.*://')
  curl -sSk -o /dev/null --retry 15 --retry-connrefused --retry-delay 1 "https://127.0.0.1:$https_port/" || {
    docker logs "$run_id-prod" 2>&1 | tail -n 10
    return 1
  }
}

# What an answer's headers were, one per line, without the carriage returns, the names in lower case
# (Git Bash's grep aborts on some of these lines with -i and -x together).
headers_of() { # headers_of <url> [curl arguments...]
  url=$1
  shift
  curl -sSk -o /dev/null -D - "$@" "$url" | tr -d '\r' | sed 's/^\([^: ]*\):/\L\1:/'
}

# Each header once, with the spec's value: twice would mean a location added its own on top.
has_the_headers() { # has_the_headers <url> [curl arguments...]
  headers_of "$@" >"$scratch/headers.txt" || return 1
  missing=0
  rm -f "$scratch/missing.txt"
  for expected in 'x-content-type-options: nosniff' 'x-frame-options: DENY' \
    'referrer-policy: strict-origin-when-cross-origin' \
    'permissions-policy: camera=(), microphone=(), geolocation=()' \
    "content-security-policy: $csp"; do
    count=$(grep -cF -- "${expected%%:*}:" "$scratch/headers.txt")
    if [ "$count" != 1 ] || ! grep -qxF -- "$expected" "$scratch/headers.txt"; then
      echo "wanted once: $expected" >>"$scratch/missing.txt"
      missing=1
    fi
  done
  [ "$missing" = 0 ] || {
    grep -v "^content-security-policy" "$scratch/headers.txt" | tail -n 6
    cat "$scratch/missing.txt"
    return 1
  }
}

lacks_header() { # lacks_header <name> <url> [curl arguments...]
  name=$1
  shift
  headers_of "$@" >"$scratch/headers.txt" || return 1
  if grep "^$name:" "$scratch/headers.txt"; then
    return 1
  fi
}

main_script() {
  curl -sS "$web_url/" | sed -n 's/.*src="\(main-[^"]*\.js\)".*/\1/p' | head -n 1
}

a_script_has_the_headers() {
  script=$(main_script)
  [ -n "$script" ] || {
    echo "no main script in index.html"
    return 1
  }
  has_the_headers "$web_url/$script"
}

# 31 MB is over client_max_body_size: nginx's own 413, which only always gives the headers to.
big_upload() {
  [ -f "$scratch/body.bin" ] || dd if=/dev/zero of="$scratch/body.bin" bs=1048576 count=31 2>/dev/null
}

refused_upload_has_the_headers() {
  big_upload || return 1
  is 413 "$(curl -sS -o /dev/null -w '%{http_code}' --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:' "$web_url/api/attachments")" || return 1
  has_the_headers "$web_url/api/attachments" --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:'
}

prod_redirects_without_hsts() {
  is "301 https://kanabol.example/projects?page=2" \
    "$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' -H 'Host: kanabol.example' "http://127.0.0.1:$http_port/projects?page=2")" || return 1
  lacks_header strict-transport-security "http://127.0.0.1:$http_port/projects" -H 'Host: kanabol.example'
}

prod_sends_hsts() { # prod_sends_hsts <path>
  headers_of "https://127.0.0.1:$https_port$1" >"$scratch/headers.txt" || return 1
  is 1 "$(grep -c '^strict-transport-security:' "$scratch/headers.txt")" || return 1
  grep -qxF "strict-transport-security: $hsts" "$scratch/headers.txt" || {
    cat "$scratch/headers.txt"
    return 1
  }
}

# What the CSP would block in the page: an inline script, an inline handler, a stylesheet that only a
# script would take off media="print" (Angular's critical CSS inlining does that), or anything from
# another origin.
page_obeys_the_csp() { # page_obeys_the_csp <index.html>
  node -e '
    const html = require("fs").readFileSync(process.argv[1], "utf8");
    const problems = [];
    for (const tag of html.match(/<script\b[^>]*>/g) ?? []) if (!/\ssrc=/.test(tag)) problems.push("inline script: " + tag);
    const handler = html.match(/<[^>]*\son[a-z]+\s*=[^>]*>/i);
    if (handler) problems.push("inline handler: " + handler[0]);
    for (const tag of html.match(/<link\b[^>]*>/g) ?? []) {
      if (/rel="?stylesheet/.test(tag) && /media="?print/.test(tag)) problems.push("stylesheet waiting on a script: " + tag);
      if (/href="?(https?:)?\/\//.test(tag)) problems.push("another origin: " + tag);
    }
    for (const tag of html.match(/<script\b[^>]*>/g) ?? []) if (/src="?(https?:)?\/\//.test(tag)) problems.push("another origin: " + tag);
    if (!/<script[^>]*src="main-/.test(html)) problems.push("no main script");
    if (!/<link[^>]*rel="?stylesheet[^>]*href="?styles-/.test(html)) problems.push("no stylesheet");
    if (problems.length) { console.log(problems.join("\n")); process.exit(1); }
  ' "$(native "$1")"
}

built_page_obeys_the_csp() {
  curl -sS "$web_url/" >"$scratch/index.html" || return 1
  page_obeys_the_csp "$scratch/index.html"
}

styles_come_from_here() {
  docker run --rm --entrypoint sh "$web_image" -c 'cat /usr/share/nginx/html/*.css' >"$scratch/styles.css" || return 1
  grep -q 'woff2' "$scratch/styles.css" || {
    echo "no font in the stylesheet"
    return 1
  }
  if grep -oE 'url\((["'"'"']?)(https?:)?//[^)]*\)|@import[^;]*' "$scratch/styles.css"; then
    return 1
  fi
}

# Controls, each against a copy that does what the checks above must catch.
location_header_control() {
  sed 's|^location /api/ {|location /api/ {\n  add_header X-Probe 1;|' frontend/nginx-app.conf >"$scratch/location-header.conf"
  grep -q 'X-Probe' "$scratch/location-header.conf" || return 1
  ! headers_at_server_level "$scratch/location-header.conf" 0 >/dev/null || return 1
  start_web location-header "$scratch/location-header.conf" || return 1
  ! has_the_headers "$web_url/api/orders" >/dev/null
}

without_always_control() {
  sed 's/ always;$/;/' frontend/nginx-app.conf >"$scratch/without-always.conf"
  start_web without-always "$scratch/without-always.conf" || return 1
  has_the_headers "$web_url/" >/dev/null || return 1
  big_upload || return 1
  ! has_the_headers "$web_url/api/attachments" --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:' >/dev/null
}

inline_script_control() {
  sed -e 's|<link rel="stylesheet" href="\(styles-[^"]*\)"[^>]*>|<link rel="stylesheet" href="\1" media="print" data-beasties-media="all">|' \
    -e "s|</body>|<script>document.querySelectorAll('link[data-beasties-media]').forEach(function(l){l.media='all'})</script></body>|" \
    "$scratch/index.html" >"$scratch/inline-script.html"
  ! page_obeys_the_csp "$scratch/inline-script.html" >/dev/null
}

task_1() {
  echo "19.1 nginx's headers, the CSP and HSTS"
  check "Docker is running" docker_is_running
  check "nginx-app.conf, nginx.conf and nginx.prod.conf are tracked" \
    sh -c 'for f in frontend/nginx-app.conf frontend/nginx.conf frontend/nginx.prod.conf; do git ls-files --error-unmatch "$f" >/dev/null || exit 1; done'
  check "nginx-app.conf sets the five headers, always" app_conf_has_the_headers
  check "every add_header in nginx-app.conf is at server level" headers_at_server_level frontend/nginx-app.conf 0
  check "every add_header in nginx.prod.conf is at server level" headers_at_server_level frontend/nginx.prod.conf 1
  check "HSTS is only in nginx.prod.conf, in the server on 443" hsts_only_in_the_443_server
  check "the image builds" build_web_image
  start_web web >"$scratch/start-web.log" 2>&1 || cat "$scratch/start-web.log"
  check "the app has the headers, once each" has_the_headers "$web_url/"
  check "a deep link has them" has_the_headers "$web_url/projects/KAN/board"
  check "a script has them" a_script_has_the_headers
  check "the manifest, in a location of its own, has them" has_the_headers "$web_url/manifest.webmanifest"
  check "what the API answers through nginx has them" has_the_headers "$web_url/api/orders?page=1"
  check "nginx's own 413 has them" refused_upload_has_the_headers
  check "the development server sends no HSTS" lacks_header strict-transport-security "$web_url/"
  check "the built page has nothing the CSP blocks" built_page_obeys_the_csp
  check "the styles and fonts come from this origin" styles_come_from_here
  start_prod_web >"$scratch/start-prod.log" 2>&1 || cat "$scratch/start-prod.log"
  check "prod sends port 80 to HTTPS, without HSTS" prod_redirects_without_hsts
  check "prod sends HSTS on 443, a year, without preload" prod_sends_hsts /
  check "and on what the API answers" prod_sends_hsts /api/orders
  check "prod has the five headers on 443" has_the_headers "https://127.0.0.1:$https_port/"
  check "and on what the API answers" has_the_headers "https://127.0.0.1:$https_port/api/orders"
  check "control: an add_header in a location takes the others off it" location_header_control
  check "control: without always, nginx's 413 has none" without_always_control
  check "control: the build's inline script would be blocked" inline_script_control
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
