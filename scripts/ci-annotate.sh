#!/bin/sh
# Turns what failed in a CI job into GitHub annotations (docs/ci.md), which anyone can read on the run's page
# and through the API, even signed out, unlike the job's log.
#
# Usage: scripts/ci-annotate.sh <log folder of scripts/gate.sh, or the cache of scripts/gate-prepare.sh>
# Annotates: changes in the checkout, each FAIL of summary.txt with the failed checks of its log and the lines
# under them, and each step of the preparation that didn't exit 0 with the end of its log.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 0
folder=${1:-}

# GitHub's annotation format: a newline is %0A, and % itself %25.
annotate() { # annotate <title> <text>
  text=$(printf '%s' "$2" | sed 's/\x1b\[[0-9;]*m//g' | head -c 3500 | sed 's/%/%25/g' | awk '{printf "%s%%0A", $0}')
  printf '::error title=%s::%s\n' "$1" "$text"
}

changes=$(git status --short | head -n 20)
[ -z "$changes" ] || annotate 'the checkout changed' "$changes
$(git diff --stat | tail -n 5)
$(git diff | head -n 40)"

[ -n "$folder" ] && [ -d "$folder" ] || exit 0

if [ -f "$folder/summary.txt" ]; then
  grep -E '^(FAIL|WARN)' "$folder/summary.txt" | while read -r verdict rest; do
    label=${rest%% (*}
    log="$folder/$label.log"
    details=
    [ -f "$log" ] && details=$(grep -E -A 12 '^  FAIL ' "$log" | head -n 60)
    [ -n "$details" ] || { [ -f "$log" ] && details=$(tail -n 30 "$log"); }
    annotate "$verdict $label" "$rest
${details:-no log}"
  done
fi

for status in "$folder"/*.status; do
  [ -f "$status" ] || continue
  [ "$(cat "$status")" = 0 ] && continue
  name=$(basename "$status" .status)
  annotate "prepare: $name exited $(cat "$status")" "$(tail -n 40 "$folder/$name.log" 2>/dev/null)"
done
exit 0
