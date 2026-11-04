#!/bin/sh
# Creates the HTTPS certificates in certs/, which git ignores (docs/SPEC.md section 13.4).
# The first run creates the internal CA, whose ca.crt colleagues import once: see docs/tls.md.
# Later runs keep that CA and only renew the server's certificate, so nobody imports anything again.
#
# Usage: scripts/make-certs.sh <server name or IP>...
# Example: scripts/make-certs.sh kanabol.local kanabol 10.0.0.10

set -eu
cd "$(dirname "$0")/.."

if [ "$#" -eq 0 ]; then
  echo "Usage: scripts/make-certs.sh <server name or IP>..." >&2
  echo "List every name and address colleagues will use to reach the server." >&2
  exit 2
fi

# Git Bash would otherwise turn the /CN=... subjects into Windows paths.
export MSYS_NO_PATHCONV=1

names=
for name in "$@"; do
  case "$name" in
    *[!0-9.]*) entry="DNS:$name" ;;
    *) entry="IP:$name" ;;
  esac
  names="${names:+$names,}$entry"
done

mkdir -p certs

if [ -f certs/ca.key ] && [ -f certs/ca.crt ]; then
  echo "Keeping the existing CA."
elif [ -f certs/ca.key ] || [ -f certs/ca.crt ]; then
  echo "certs/ has only half of the CA. Restore the missing file, or delete the other one to start over." >&2
  exit 1
else
  openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
    -keyout certs/ca.key -out certs/ca.crt -subj "/CN=Kanabol Internal CA" 2>/dev/null
  chmod 600 certs/ca.key
  echo "Created the CA. Give certs/ca.crt to colleagues, and keep certs/ca.key on this server."
fi

openssl req -newkey rsa:2048 -nodes -keyout certs/server.key -out certs/server.csr \
  -subj "/CN=$1" 2>/dev/null
chmod 600 certs/server.key
printf 'subjectAltName=%s\n' "$names" >certs/san.ext
openssl x509 -req -in certs/server.csr -CA certs/ca.crt -CAkey certs/ca.key -CAcreateserial \
  -out certs/server.crt -days 825 -sha256 -extfile certs/san.ext 2>/dev/null
rm -f certs/server.csr certs/san.ext

echo "Created the server certificate for: $names"
