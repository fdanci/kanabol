#!/bin/sh
# Stops with a clear message when Docker isn't running. The full suite's tests need it, and without this
# all they report is a list of failed tests, with no mention of Docker.

# Shells opened before Docker Desktop was installed don't have it on the PATH.
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"

docker info >/dev/null 2>&1 && exit 0

echo "✖ Docker isn't running, and the full test suite needs it. Start Docker Desktop, then try again." >&2
exit 1
