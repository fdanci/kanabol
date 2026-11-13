#!/bin/sh
# What .github/workflows/ci.yml runs on this push (docs/ci.md, docs/SPEC.md §14.7). Writes tier= and shards= (a
# JSON list of {"name", "scripts"}, each a job of scripts/gate.sh) to $GITHUB_OUTPUT, or prints them.
#
#   full   on a push to main, and only then (docs/SPEC.md §14.7, §14.8): every scripts/check-*.sh, in the shards
#          below. Nothing starts it by hand: ci.yml has no workflow_dispatch, and a commit's subject decides nothing
#   short  on any other push: check-tests, check-lint and each check-*.sh the push changed, a job each
#
# Usage: scripts/ci-plan.sh <event name> <ref> <commit before the push, or nothing>
# Needs the history of the push (actions/checkout with fetch-depth: 0).

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# The event decides nothing any more, only the ref: it stays in the usage, as ci.yml passes it.
event=${1:?usage: scripts/ci-plan.sh <event> <ref> [before]}
ref=${2:?usage: scripts/ci-plan.sh <event> <ref> [before]}
before=${3:-}

# The full gate's jobs, of about the same length each (the times of the gate of group 23, on Windows):
# a script added to the repo has to be put in one, or the plan stops.
full_shards='
tests=check-tests
hardening=check-hardening check-foundation check-permissions
pipeline=check-pipeline check-lint check-web check-ui
columns=check-columns check-collation check-admin
board=check-board check-reports
tasks=check-tasks check-history
assignees=check-assignees check-alerts
backend=check-backend check-comments check-dashboard
users=check-users check-release check-projects
realtime=check-realtime check-auth check-docker:1,2,3,4,6 check-docker:5
'

json() { # json <name=scripts lines>: the shards as JSON
  printf '%s\n' "$1" | sed '/^$/d' | node -e '
    const lines = require("fs").readFileSync(0, "utf8").trim().split("\n");
    const shards = lines.map((l) => { const [name, scripts] = l.split("="); return { name, scripts }; });
    process.stdout.write(JSON.stringify(shards));
  '
}

if [ "$ref" = refs/heads/main ]; then
  tier=full
  missing=
  for script in scripts/check-*.sh; do
    name=$(basename "$script" .sh)
    printf '%s\n' "$full_shards" | tr ' =' '\n\n' | sed 's/:.*//' | grep -qx "$name" || missing="$missing $name"
  done
  [ -z "$missing" ] || {
    echo "Not in any shard of scripts/ci-plan.sh:$missing"
    exit 1
  }
  shards=$(json "$full_shards")
else
  tier=short
  # The push's own commits; for a new branch, those dev doesn't have.
  if [ -z "$before" ] || ! git cat-file -e "$before^{commit}" 2>/dev/null; then
    before=$(git merge-base HEAD origin/dev 2>/dev/null || git rev-parse 'HEAD~1' 2>/dev/null || echo)
  fi
  changed=
  if [ -n "$before" ]; then
    changed=$(git diff --name-only "$before" HEAD -- 'scripts/check-*.sh' | while read -r file; do
      [ -f "$file" ] && basename "$file" .sh
    done)
  fi
  lines=
  for name in check-tests check-lint $changed; do
    printf '%s\n' "$lines" | grep -q "^$name=" || lines="$lines
$name=$name"
  done
  shards=$(json "$lines")
fi

echo "tier=$tier"
echo "shards=$shards"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "tier=$tier" >>"$GITHUB_OUTPUT"
  echo "shards=$shards" >>"$GITHUB_OUTPUT"
fi
