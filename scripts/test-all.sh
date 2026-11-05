#!/bin/sh
set -e
cd "$(git rev-parse --show-toplevel)"
echo "▶ backend: build + all tests (Testcontainers needs Docker running)"
dotnet build backend/Kanabol.sln -warnaserror --nologo -v q
dotnet test backend/Kanabol.sln --no-build --nologo -v q
echo "▶ frontend: lint + tests"
cd frontend
npx ng lint
npx ng test --no-watch
echo "✔ all green"
