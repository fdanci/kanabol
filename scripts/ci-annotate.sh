#!/bin/sh
# Turns a CI job's results into GitHub annotations (docs/ci.md), which anyone can read on the run's page and
# through the API, even signed out, unlike the job's log.
#
# Usage: scripts/ci-annotate.sh [--summary <title>] <log folder of scripts/gate.sh, or the cache of
#        scripts/gate-prepare.sh>
# Without --summary, errors: changes in the checkout, each FAIL or WARN of summary.txt with the failed checks
# of its log and the lines under them, and each step of the preparation that didn't exit 0 with the end of its
# log. With --summary, one notice: summary.txt, or the preparation's steps, with their times.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 0
summary=
if [ "${1:-}" = --summary ]; then
  summary=${2:-summary}
  shift 2
fi
folder=${1:-}

# GitHub's annotation format: a newline is %0A, and % itself %25.
annotate() { # annotate <level> <title> <text>
  text=$(printf '%s\n' "$3" | sed 's/\x1b\[[0-9;]*m//g' | head -c 3500 | sed 's/%/%25/g' | awk '{printf "%s%%0A", $0}')
  printf '::%s title=%s::%s\n' "$1" "$2" "$text"
}

if [ -n "$summary" ]; then
  if [ -f "$folder/summary.txt" ]; then
    annotate notice "$summary" "$(cat "$folder/summary.txt")"
  elif [ -f "$folder/prepare.txt" ]; then
    annotate notice "$summary" "$(cat "$folder/prepare.txt")"
  fi
  exit 0
fi

changes=$(git status --short | head -n 20)
[ -z "$changes" ] || annotate error 'the checkout changed' "$changes
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
    annotate error "$verdict $label" "$rest
${details:-no log}"
  done
fi

for status in "$folder"/*.status; do
  [ -f "$status" ] || continue
  [ "$(cat "$status")" = 0 ] && continue
  name=$(basename "$status" .status)
  log="$folder/$name.log"
  # A failed test, a build error or a crash, with the lines after it; else the end of the log.
  details=$(grep -E -A 8 '^ *Failed |^Failed!|error [A-Z]+[0-9]+|Test Run Aborted|crashed|FAIL ' "$log" 2>/dev/null | head -n 70)
  [ -n "$details" ] || details=$(tail -n 40 "$log" 2>/dev/null)
  annotate error "prepare: $name exited $(cat "$status")" "$details"
done
exit 0
