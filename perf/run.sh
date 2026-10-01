#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

compose=(docker compose -p tree-editor-perf -f docker-compose.yml -f perf/docker-compose.perf.yml)
psql=("${compose[@]}" exec -T db sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At')

trap '"${compose[@]}" down -v >/dev/null 2>&1' EXIT

echo "Starting the stack (project tree-editor-perf, http://localhost:8080)..." >&2
"${compose[@]}" up --build -d --wait >&2

echo "Seeding about 1,000,000 nodes..." >&2
"${psql[@]}" < perf/seed-large-tree.sql

ids=$("${psql[@]}" < perf/benchmark-ids.sql)

echo "Running the HTTP benchmark..." >&2
TREE_EDITOR_BENCH_IDS="$ids" node perf/http-bench.mjs

echo
echo "Running the client cache benchmark..." >&2
node perf/core-bench.mjs
