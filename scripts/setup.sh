#!/bin/sh
# Prepares a clone: the packages, the hooks and the git settings they rely on (docs/SPEC.md section 14.6).
# Usage: scripts/setup.sh    (safe to run again)

set -e
cd "$(git rev-parse --show-toplevel)"

# The root package's prepare script makes git use the hooks in .husky/.
npm install --no-audit --no-fund
(cd frontend && npm install --no-audit --no-fund)
dotnet tool restore

# A fast-forward runs no hook, so a merge into main always has to create a commit.
git config branch.main.mergeOptions "--no-ff"
git config commit.gpgsign false

echo "Ready. The hooks are on: docs/SPEC.md section 14 says what they run."
