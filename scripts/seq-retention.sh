#!/bin/sh
# Makes Seq delete logs after 30 days (docs/SPEC.md section 8.1). Seq keeps this in its own volume,
# so it's needed once per new volume, and running it again changes nothing.
#
# Usage: SEQ_ADMIN_PASSWORD=<password> scripts/seq-retention.sh [seq-url]
# The address is http://localhost:5341 unless given. Without the variable, it asks for the password.

set -eu

url=${1:-http://localhost:5341}
retention=30.00:00:00

if [ -z "${SEQ_ADMIN_PASSWORD:-}" ]; then
  printf 'Seq admin password: ' >&2
  stty -echo
  read -r SEQ_ADMIN_PASSWORD
  stty echo
  echo >&2
fi

cookies=$(mktemp)
trap 'rm -f "$cookies"' EXIT

password=$(printf '%s' "$SEQ_ADMIN_PASSWORD" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g')
session=$(curl -sS -c "$cookies" -H 'Content-Type: application/json' \
  -d "{\"Username\":\"admin\",\"Password\":\"$password\"}" "$url/api/users/login")
token=$(printf '%s' "$session" | sed -n 's/.*"CsrfToken":"\([^"]*\)".*/\1/p')
if [ -z "$token" ]; then
  echo "Seq at $url refused the login." >&2
  exit 1
fi

policies=$(curl -sS -b "$cookies" "$url/api/retentionpolicies")
case "$policies" in
  '[]')
    curl -sS -f -o /dev/null -b "$cookies" -H "X-Seq-CsrfToken: $token" -H 'Content-Type: application/json' \
      -d "{\"RetentionTime\":\"$retention\",\"DataSource\":\"Stream\"}" "$url/api/retentionpolicies"
    echo "Seq at $url now deletes logs after 30 days."
    ;;
  *"\"RetentionTime\":\"$retention\""*)
    echo "Seq at $url already deletes logs after 30 days."
    ;;
  *)
    echo "Seq at $url has another retention policy, so nothing changed: $policies" >&2
    exit 1
    ;;
esac
