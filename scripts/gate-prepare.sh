#!/bin/sh
# What the full gate does once, for every check script to reuse (docs/SPEC.md §14.7): the solution built
# with warnings as errors from scratch, dotnet format's check, the frontend's lint, prettier's check and
# production build (with stats.json), and scripts/test-all.sh, the whole suite, run once with TRX results for
# each test project and a JSON report of the specs. scripts/gate.sh calls it and points the check scripts at
# the cache through KANABOL_GATE_CACHE; scripts/gate-reuse.mjs answers from it.
#
# Usage: scripts/gate-prepare.sh <cache folder>
# Needs a clean checkout after scripts/setup.sh, and Docker running (the suite's tests). Each result is kept
# as it came, failures too; only a failed scripts/test-all.sh isn't reused, so check-tests.sh 8 runs it again.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

cache=${1:?usage: scripts/gate-prepare.sh <cache folder>}
native() { cygpath -m "$1" 2>/dev/null || echo "$1"; }

[ -z "$(git status --porcelain)" ] || {
  echo "The checkout has changes: the cache would describe another tree."
  git status --short | head -n 10
  exit 2
}
rm -rf "$cache"
mkdir -p "$cache/trx" "$cache/shim" || exit 2

step() { # step <name> <command...>: its output in <name>.log, its exit code in <name>.status
  name=$1
  shift
  started=$(date +%s)
  "$@" >"$cache/$name.log" 2>&1
  echo "$?" >"$cache/$name.status"
  printf '  %-9s exit %s, %s s\n' "$name" "$(cat "$cache/$name.status")" "$(($(date +%s) - started))" |
    tee -a "$cache/prepare.txt"
}

echo "Preparing the gate's cache in $cache"
step build dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
step format dotnet format backend/Kanabol.sln --verify-no-changes
step lint sh -c 'cd frontend && npx ng lint'
step prettier sh -c 'cd frontend && npx prettier --check .'
step ngbuild sh -c "cd frontend && npx ng build --stats-json --output-path '$(native "$cache/dist")'"

# scripts/test-all.sh as it is, once. dotnet test reads VSTestLogger and VSTestResultsDirectory from the
# environment (the %3B is a ; that MSBuild would otherwise split on), and a stand-in npx adds a JSON reporter
# to the specs' run, the default one still printing what it always does.
real_npx=$(command -v npx)
cat >"$cache/shim/npx" <<SH
#!/bin/sh
if [ "\$*" = "ng test --no-watch" ]; then
  exec "$real_npx" ng test --no-watch --reporters=default --reporters=json --output-file="$(native "$cache/specs.json")"
fi
exec "$real_npx" "\$@"
SH
chmod +x "$cache/shim/npx"
step test-all env PATH="$cache/shim:$PATH" VSTestLogger='trx%3BLogFilePrefix=suite' \
  VSTestResultsDirectory="$(native "$cache/trx")" scripts/test-all.sh

# The test projects, and a TRX file holding each one's results; without them every test runs for real.
ls backend/tests | while read -r project; do
  [ -f "backend/tests/$project/$project.csproj" ] && echo "$project"
done >"$cache/test-projects"
# Each project's results have to be all of them: as many as dotnet test reported in its Total.
ls -l "$cache/trx" | sed 's/^/  /' | tee -a "$cache/prepare.txt"
missing=
while read -r project; do
  reported=$(sed 's/\x1b\[[0-9;]*m//g' "$cache/test-all.log" |
    sed -n "s/.*Total: *\([0-9]*\),.* - $project\.dll .*/\1/p" | tail -n 1)
  parsed=$(node scripts/gate-reuse.mjs trx-count "$(native "$cache/trx")" "$project")
  printf '  %-26s %s results in TRX, %s reported\n' "$project" "${parsed:-none}" "${reported:-none}" |
    tee -a "$cache/prepare.txt"
  [ -n "$reported" ] && [ "$parsed" = "$reported" ] || missing="$missing $project"
done <"$cache/test-projects"
if [ -z "$missing" ]; then
  touch "$cache/trx-ok"
else
  echo "  the TRX results don't hold every test of:$missing; every test runs for real" | tee -a "$cache/prepare.txt"
fi
[ -s "$cache/specs.json" ] || {
  rm -f "$cache/specs.json"
  echo "  no JSON report of the specs; they will run for real"
}
[ "$(cat "$cache/test-all.status")" = 0 ] || {
  echo "  scripts/test-all.sh failed: check-tests.sh 8 runs it again for real"
  tail -n 20 "$cache/test-all.log"
}

git rev-parse 'HEAD^{tree}' >"$cache/tree"
native "$(pwd -P)" >"$cache/root"
node scripts/gate-reuse.mjs environment >"$cache/environment" || exit 2
[ -z "$(git status --porcelain)" ] || {
  echo "The preparation changed the checkout; nothing of it will be reused."
  git status --short | head -n 10
  exit 1
}
touch "$cache/ready"
echo "Ready: $cache"
