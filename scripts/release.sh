#!/bin/sh
# Builds the production images of a version, on the server, after git pull (docs/SPEC.md section 13.5,
# docs/deploy.md). There's no image registry: the server builds kanabol-api and kanabol-web from its clone,
# with the tag compose.prod.yaml runs, and the database's image, kanabol-postgres with pgvector, with the
# tag compose.yaml gives it (docs/SPEC.md section 19.1), so compose.prod.yaml builds nothing.
#
# The database's tag is PostgreSQL's and pgvector's versions, not Kanabol's. Its build is labelled with the
# git tree of database/, and built again only when that changes: each build is a new image, and compose
# would restart PostgreSQL for it at every release.
#
# Usage: scripts/release.sh vX.Y.Z
# Example: scripts/release.sh v0.1.2
# It refuses unless the clone is on main, clean, at a commit origin/main has, whose APP_VERSION
# (frontend/src/app/core/layout/sidenav.ts, what the sidebar shows) and default tag in compose.prod.yaml
# are that version: the commit a version tags sets both (docs/SPEC.md section 13.5). It builds the
# database's image if database/ changed, then kanabol-api and kanabol-web with that tag, puts the annotated
# git tag on the commit if it's missing (one on another commit stops it), and writes KANABOL_VERSION in
# .env. It doesn't start anything: it prints the command that does. Running it again for the same version
# builds api and web again and changes nothing else.

set -u
cd "$(dirname "$0")/.." || exit 2

command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
export MSYS_NO_PATHCONV=1

refuse() {
  echo "$1" >&2
  echo "Nothing was built, tagged or changed." >&2
  exit 1
}

version=${1:-}
if [ "$#" -ne 1 ] || ! printf '%s' "$version" | grep -Eq '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'; then
  echo "Usage: scripts/release.sh vX.Y.Z" >&2
  exit 2
fi

branch=$(git symbolic-ref --short -q HEAD)
[ "$branch" = main ] || refuse "The clone is on ${branch:-a detached HEAD}, not main: git checkout main && git pull"
[ -z "$(git status --porcelain)" ] || refuse "The clone has changes git doesn't have, which the images would hold: git status"
git rev-parse -q --verify refs/remotes/origin/main >/dev/null || refuse "There's no origin/main to compare with: git fetch"
git merge-base --is-ancestor HEAD refs/remotes/origin/main ||
  refuse "main has commits origin/main doesn't: release only what was pushed"
commit=$(git rev-parse HEAD)

# The version the sidebar shows and the one compose.prod.yaml runs without KANABOL_VERSION, from the
# clean tree the images are built from.
app_version=$(sed -n "s/^export const APP_VERSION = '\([^']*\)';\$/\1/p" \
  frontend/src/app/core/layout/sidenav.ts 2>/dev/null | tr -d '\r')
default_tag() { # default_tag <api|web>
  sed -n -E 's/^ +image: kanabol-'"$1"':\$\{KANABOL_VERSION:-([^}]*)\}$/\1/p' compose.prod.yaml 2>/dev/null | tr -d '\r'
}
api_tag=$(default_tag api)
web_tag=$(default_tag web)
mismatch=
if [ "$app_version" != "${version#v}" ]; then
  echo "APP_VERSION in frontend/src/app/core/layout/sidenav.ts is ${app_version:-missing}, not ${version#v}." >&2
  mismatch=1
fi
if [ "$api_tag" != "$version" ] || [ "$web_tag" != "$version" ]; then
  echo "compose.prod.yaml runs kanabol-api:${api_tag:-missing} and kanabol-web:${web_tag:-missing} without KANABOL_VERSION, not $version." >&2
  mismatch=1
fi
[ -z "$mismatch" ] || refuse "The commit tagged $version sets both to that version, as docs/deploy.md says."

database_image=$(sed -n -E 's/^ +image: (kanabol-postgres:[^ ]+)$/\1/p' compose.yaml 2>/dev/null | tr -d '\r')
[ -n "$database_image" ] || refuse "compose.yaml names no kanabol-postgres image for its postgres service."
database_tree=$(git rev-parse -q --verify HEAD:database) || refuse "The commit has no database/ to build kanabol-postgres from."

if git rev-parse -q --verify "refs/tags/$version" >/dev/null; then
  tagged=$(git rev-list -n 1 "refs/tags/$version")
  [ "$tagged" = "$commit" ] || refuse "The tag $version is on $(git rev-parse --short "$tagged"), not on main's $(git rev-parse --short HEAD)."
  new_tag=
else
  git config user.email >/dev/null || refuse "git has no user.email here, which the annotated tag needs: git config user.email ..."
  new_tag=1
fi

[ -f .env ] || refuse "There's no .env: copy .env.example to .env and fill it in, as docs/deploy.md says."
docker info >/dev/null 2>&1 || refuse "Docker isn't running."

built_tree=$(docker image inspect -f '{{index .Config.Labels "kanabol.database-tree"}}' "$database_image" 2>/dev/null | tr -d '\r')
if [ "$built_tree" = "$database_tree" ]; then
  echo "$database_image is already built from this database/."
else
  echo "Building $database_image..."
  docker build -t "$database_image" --label "kanabol.database-tree=$database_tree" database ||
    refuse "The build of $database_image failed."
fi

for part in api web; do
  case "$part" in
    api) context=backend ;;
    web) context=frontend ;;
  esac
  echo "Building kanabol-$part:$version..."
  docker build -t "kanabol-$part:$version" \
    --label "org.opencontainers.image.version=$version" \
    --label "org.opencontainers.image.revision=$commit" \
    "$context" || refuse "The build of kanabol-$part failed."
done

if [ -n "$new_tag" ]; then
  git tag -a "$version" -m "Kanabol $version" || {
    echo "Both images are built, but the tag $version wasn't made." >&2
    exit 1
  }
  echo "Tagged $commit as $version. It's only on this server: git push origin $version shares it."
fi

# Only that line of .env changes, and nothing of it is printed.
if grep -q '^KANABOL_VERSION=' .env; then
  # The copy holds the secrets too, so only this user reads it, and .env keeps its own mode.
  (umask 077 && sed "s/^KANABOL_VERSION=.*/KANABOL_VERSION=$version/" .env >.env.release) && cat .env.release >.env
  status=$?
  rm -f .env.release
  [ "$status" -eq 0 ] || {
    echo "Both images are built, but .env wasn't changed." >&2
    exit 1
  }
else
  [ -z "$(tail -c 1 .env)" ] || echo >>.env
  echo "KANABOL_VERSION=$version" >>.env
fi

echo "Built kanabol-api:$version and kanabol-web:$version from $(git rev-parse --short HEAD), with $database_image, and set KANABOL_VERSION=$version in .env."
echo "Start them, after a backup (scripts/backup.sh) if the stack holds data:"
echo "  docker compose -f compose.yaml -f compose.prod.yaml up -d"
