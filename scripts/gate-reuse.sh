# Sourced by a check script when KANABOL_GATE_CACHE is set, which scripts/gate.sh does (docs/SPEC.md §14.7):
# dotnet and npx become functions that answer a build, a format check, a test run, a lint, a prettier check
# or a production build from what scripts/gate-prepare.sh did once for the whole gate, and run everything
# else for real. scripts/gate-reuse.mjs decides; when in doubt, the command runs. Without KANABOL_GATE_CACHE
# nothing here is sourced, and the script runs as it does on its own.

_gate_reuse="$(pwd -P)/scripts/gate-reuse.mjs"

dotnet() {
  case "${1:-}" in
    build | test | format | run | publish | clean | msbuild) ;;
    *)
      command dotnet "$@"
      return
      ;;
  esac
  node "$_gate_reuse" dotnet "$@"
  _gate_status=$?
  [ "$_gate_status" -eq 99 ] || return "$_gate_status"
  command dotnet "$@"
  _gate_status=$?
  node "$_gate_reuse" ran "$_gate_status" dotnet "$@"
  return "$_gate_status"
}

npx() {
  case "${1:-}" in
    ng | prettier) ;;
    *)
      command npx "$@"
      return
      ;;
  esac
  node "$_gate_reuse" npx "$@"
  _gate_status=$?
  [ "$_gate_status" -eq 99 ] || return "$_gate_status"
  command npx "$@"
}
