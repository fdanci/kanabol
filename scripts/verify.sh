#!/bin/sh
# What the commit and merge hooks run (docs/SPEC.md section 14.3): the same fast checks on every branch, main
# too. The full suite runs in the GitHub workflow (docs/ci.md), or by hand with scripts/test-all.sh.
# Usage: scripts/verify.sh [commit|merge]

set -e
cd "$(git rev-parse --show-toplevel)"
branch=$(git rev-parse --abbrev-ref HEAD)
mode=${1:-commit}

echo "▶ $mode: lint-staged"
# eslint, prettier and dotnet format on the staged files only. dotnet format ignores absolute paths
# without saying so, and lint-staged sends those unless told otherwise.
npx --no -- lint-staged --relative

echo "▶ $branch: fast checks"
# The two sides at once when a commit touches both. The backend's output waits in a file and comes after
# the specs', so the two don't mix; either one failing stops the commit.
failed=0
backend_log=
if git diff --cached --name-only | grep -q '^backend/'; then
  backend_log=$(mktemp)
  (dotnet build backend/Kanabol.sln -warnaserror --nologo -v q &&
    dotnet test backend/tests/Kanabol.Domain.Tests --no-build --nologo -v q) >"$backend_log" 2>&1 &
  backend=$!
fi
if git diff --cached --name-only | grep -q '^frontend/'; then
  (cd frontend && npx ng test --no-watch) || failed=1
fi
if [ -n "$backend_log" ]; then
  wait "$backend" || failed=1
  cat "$backend_log"
  rm -f "$backend_log"
fi
exit "$failed"
