# Tree Editor with a Local Cache

[![Coverage](https://raw.githubusercontent.com/dlevchenko-olearis/tree-editor/badges/coverage.svg)](https://github.com/dlevchenko-olearis/tree-editor/actions/workflows/ci.yml)

A web application for editing a large, arbitrarily deep tree stored in PostgreSQL.
**DBTreeView** browses the database lazily, one page of children at a time. **CachedTreeView**
holds individually loaded nodes, assembles their hierarchy by itself, and keeps edits, additions,
and deletions local until **Apply** sends them to the server in one atomic request.

## Quick start

```sh
docker compose up --build
```

Open <http://localhost:8080>. The first start creates the schema and loads the sample tree:
6 levels deep, with one node of 25 children (more than one page). **Reset** in the toolbar
restores the sample data at any time; `docker compose down -v` removes the database.

Only `127.0.0.1:8080` is published. Database credentials default to `.env.example`;
copy it to `.env` to change them.

## Stack

| Part | Technology |
|---|---|
| UI | React 19, TypeScript 6 (strict), Vite 8, TanStack Virtual |
| API | .NET 10, ASP.NET Core minimal APIs, EF Core 10 with Npgsql |
| Database | PostgreSQL 18 |
| Delivery | Docker Compose: PostgreSQL, API, nginx (serves the SPA, proxies `/api`); non-root containers |
| Tests | xUnit v3, Testcontainers, NetArchTest, Vitest, Testing Library |

## Architecture

```
 browser                                        server
 +------------------------------------+         +------------------------------------+
 | ui/        React views             |         | Api             endpoints, limits, |
 |              |                     |         |                 error mapping      |
 |              v                     |         |   |                                |
 | core/      TreeCache, DbTreeBrowser|         |   v                                |
 |            ChangeSetBuilder        |         | Application     use cases, ports,  |
 |              |                     |  HTTP   |                 decorators         |
 |              v                     |  flat   |   |                                |
 | ports/     TreeApi, DbTreeApi,     |  JSON   |   v                                |
 |            ResetApi                |         | Domain          ChangeSet,         |
 | adapters/  HttpTreeApi ------------+-------->|                 ApplyPlanner       |
 +------------------------------------+ (nginx) |   ^                                |
                                                | Infrastructure  PostgreSQL adapters|
                                                +-------------------+----------------+
                                                                    |
                                                               PostgreSQL
```

- **Ports and adapters on both sides.** The cache reaches the server only through `TreeApi`
  (`loadNode`, `apply`); DBTreeView uses the read-only `DbTreeApi`. That is how "the cache may
  access the database only when loading or applying" is enforced. The cache core has no React
  dependency.
- **Dependency rule.** Domain depends on nothing; Application depends on neither Infrastructure nor
  ASP.NET. Project references enforce it, and architecture tests check it.
- **Commands are decorated** with logging, then a transaction, then idempotency. Conflicts are
  returned as results, not thrown.
- **Depth never matters.** Only flat rows with a parent id cross HTTP. Every walk is iterative in
  C# and TypeScript, or a recursive CTE with `UNION` in SQL.

### Database schema

```
nodes          id uuid PK, parent_id uuid FK -> nodes, value varchar(200),
               is_deleted bool, version bigint
               index (parent_id, id)               children pages, "has children" probe
               index (id) where parent_id is null  roots page
tree_state     generation bigint                   incremented by every Reset
apply_requests key, request_hash, response         idempotency keys, kept 24 hours
node_state     view: effective deletion state of every node, for inspection
```

### Key decisions

1. **Deletion is derived from ancestors.** Deleting sets `is_deleted` on the subtree root only.
   A node counts as deleted when it or any ancestor is flagged, so deleting a subtree of any size,
   including nodes never loaded, is a one-row write. DBTreeView and the `node_state` view show every
   descendant as deleted.
2. **UUIDv7 ids are generated in the browser.** New nodes, including nested ones, reference each
   other directly, so no temporary ids need remapping. Ids sort by creation time, which gives
   keyset paging.
3. **Optimistic concurrency.** Every write takes a new version from a global sequence. An edit
   carries the version it was based on; any mismatch rejects the whole Apply and lists every
   conflict. Deletions need no version.
4. **Tree generation.** Each Reset starts a new generation. An Apply prepared before a Reset is
   rejected, and any response tells another session that it must clear its cache. Reset locks
   `apply_requests`, `tree_state`, and `nodes` exclusively, in the order every other transaction
   locks them. It waits for each Apply that has read the generation, and reads, which lock
   `tree_state` and `nodes` before taking their snapshot, never see a Reset half-done.
5. **Idempotent Apply.** Each Apply carries an `Idempotency-Key`. The client retries network
   errors, 429, and 5xx with the same key; a request whose outcome is unknown is sent again before
   any new change set, unless the user chooses **Discard unconfirmed**. Attempts that only ever got
   429 were not processed, so the change set simply stays pending.
6. **Cache model.** One `parentOf` map is filled from the ancestor chains the server returns, so
   memory grows with the number of distinct known ids, not with depth. Deletion is O(1), and
   building a change set looks only at pending ids. A node whose parent is not cached is shown at
   the top level rather than under a more distant cached ancestor, so the view never implies a
   parent-child link that does not exist.
7. **Unauthenticated API hardening.** Every POST needs a JSON content type and
   `X-Requested-With: tree-editor` (blocks cross-site forms). Also: a per-IP rate limit, checked
   after that guard so forged requests cannot use it up, an 8 MB body limit in nginx and Kestrel, at
   most 10,000 operations per Apply, and statement and lock timeouts.

## Development

```sh
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --wait db  # PostgreSQL on 127.0.0.1:5432
dotnet run --project src/TreeEditor.Api                                          # API on http://localhost:5080
cd web && npm ci && npm run dev                                                  # UI on http://localhost:5173
```

```sh
dotnet test            # 198 tests: domain, application, architecture, API against PostgreSQL (needs Docker)
cd web && npm test     # 186 tests: cache core, HTTP adapter, React components
./scripts/coverage.sh  # both suites with coverage, merged into artifacts/coverage
```

CI (`.github/workflows/ci.yml`) runs `scripts/coverage.sh` on every push and pull request. On
`main` it publishes the badge above to the `badges` branch: line coverage of the .NET and web code
together, without tests, test helpers, migrations, and generated code.

## Performance

Measured on one laptop (AMD Ryzen 3 3250U, 2 cores / 4 threads, 13 GB RAM). PostgreSQL, the API,
nginx, and the load generator all ran there, as the production Compose build, with requests going
through nginx. The database held **1,011,059 nodes** (160 MB): one root with 1,000 x 1,000
descendants, plus a chain 10,000 levels deep.

**Reads, one client**

| Request | p50, ms | p95, ms | Response |
|---|---:|---:|---:|
| Roots page (20) | 5.4 | 11.1 | 0.5 KB |
| Children page (20), first of 1,000 | 5.7 | 9.5 | 3.1 KB |
| Children page (20), last of 1,000 | 8.0 | 13.1 | 3.1 KB |
| Children page (200) | 7.2 | 12.1 | 31 KB |
| Children page (20) under a deleted subtree of 1,001,000 nodes | 7.6 | 10.9 | 3.1 KB |
| Node at depth 10 | 3.9 | 6.9 | 0.6 KB |
| Node at depth 1,000 | 11.0 | 16.2 | 38 KB |
| Node at depth 10,000 | 54.8 | 87.7 | 381 KB |
| Children page at depth 10,000 | 35.6 | 54.8 | 0.2 KB |

With 16 clients, children pages sustain 625 req/s (p95 42 ms).

**Writes**

| Apply | p50, ms | p95, ms |
|---|---:|---:|
| 1 edit | 12.3 | 18.5 |
| 1 edit, 16 clients on different nodes (236 Applies/s) | 64 | 121 |
| 100 new nodes | 15.4 | 19.9 |
| 1,000 new nodes | 81 | 109 |
| 10,000 new nodes of 200 characters (3 MB) | 615 | 652 |
| Delete a leaf (single request) | 16 | |
| Delete a subtree of 1,001,000 nodes (single request) | 10 | |
| Reset with 1.01 million rows (single request) | 62 | |

**Client cache core**, in Node against the in-memory server the frontend tests use, with 10,000
nodes:

| Operation | ms |
|---|---:|
| Load a chain one node at a time, deepest first (all 10,000 loads) | 3,159 |
| Build the visible rows of the chain (depth 9,999) | 3.7 |
| Delete the top node and resolve the status of every node | 15 |
| Build the change set of 10,000 nested new nodes, parent first | 19 |

After the chain is loaded, `parentOf` holds exactly 10,000 ids: memory follows the number of
distinct nodes, not depth.

**Memory** (from `docker stats` during the run): the API uses about 60 MB idle and 100 MB under
load, PostgreSQL about 250 to 280 MB. The UI bundle is 280 KB (86 KB gzipped).

What the numbers show: page reads do not depend on table size, a subtree deletion costs the same
as a leaf deletion, and only a single-node load grows with depth. The tables show the median of
three runs; repeated runs vary by about 20%, and the single-request rows more.

### Reproducing

```sh
./perf/run.sh
```

It needs Docker, Node.js 22.18 or later (it runs the TypeScript cache core directly), and a free
port 8080, and takes about two minutes. The script:

- builds the production stack as a separate Compose project, `tree-editor-perf`, with the POST
  rate limit lifted;
- seeds about 1,000,000 nodes (`perf/seed-large-tree.sql`);
- runs the HTTP benchmark (`perf/http-bench.mjs`) and the cache core benchmark
  (`perf/core-bench.mjs`), printing both as Markdown tables;
- removes the stack and its data.
