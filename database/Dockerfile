# The database: PostgreSQL 17 on Alpine, with pgvector built from source (docs/SPEC.md §19.1).
# Both versions are pinned. The 17-alpine tag moves, and on an Alpine with another LLVM the build
# breaks: PostgreSQL here is built with LLVM 21, and its rules compile the extension with the same clang.
# Staying on Alpine keeps the ro-x-icu collation exactly as it was under an existing database.
FROM postgres:17.11-alpine3.24

ARG PGVECTOR_VERSION=0.8.7

# One layer, so the compilers leave the image with it. OPTFLAGS="" turns off -march=native,
# so the image runs on any x86-64 machine, not only on the one that built it.
RUN apk add --no-cache --virtual .build-deps build-base git clang21 llvm21 \
    && git clone --branch "v$PGVECTOR_VERSION" --depth 1 https://github.com/pgvector/pgvector.git /tmp/pgvector \
    && make -C /tmp/pgvector OPTFLAGS="" \
    && make -C /tmp/pgvector install \
    && rm -rf /tmp/pgvector \
    && apk del .build-deps
