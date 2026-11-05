#!/bin/sh
# What the commit and merge hooks run (docs/SPEC.md section 14.3): the full suite on main, fast checks elsewhere.
# Usage: scripts/verify.sh [commit|merge]

set -e
cd "$(git rev-parse --show-toplevel)"
branch=$(git rev-parse --abbrev-ref HEAD)
mode=${1:-commit}

if [ "$branch" = "main" ]; then
  scripts/require-docker.sh
fi

echo "▶ $mode: lint-staged"
# eslint, prettier and dotnet format on the staged files only. dotnet format ignores absolute paths
# without saying so, and lint-staged sends those unless told otherwise.
npx --no -- lint-staged --relative

if [ "$branch" = "main" ]; then
  echo "▶ main: full test suite"
  exec scripts/test-all.sh
fi

echo "▶ $branch: fast checks"
if git diff --cached --name-only | grep -q '^backend/'; then
  dotnet build backend/Kanabol.sln -warnaserror --nologo -v q
  dotnet test backend/tests/Kanabol.Domain.Tests --no-build --nologo -v q
fi
if git diff --cached --name-only | grep -q '^frontend/'; then
  (cd frontend && npx ng test --no-watch)
fi
