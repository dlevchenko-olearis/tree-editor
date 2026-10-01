CREATE TEMP TABLE bulk_root AS SELECT uuidv7() AS id;

INSERT INTO nodes (id, parent_id, value)
SELECT id, NULL, 'Bulk' FROM bulk_root;

CREATE TEMP TABLE level1 AS
SELECT i, uuidv7() AS id FROM generate_series(1, 1000) AS i ORDER BY i;

INSERT INTO nodes (id, parent_id, value)
SELECT l.id, r.id, 'Group ' || l.i
FROM level1 AS l CROSS JOIN bulk_root AS r
ORDER BY l.i;

INSERT INTO nodes (id, parent_id, value)
SELECT uuidv7(), l.id, 'Item ' || l.i || '.' || g
FROM level1 AS l CROSS JOIN generate_series(1, 1000) AS g
ORDER BY l.i, g;

CREATE TEMP TABLE chain AS
SELECT i, uuidv7() AS id FROM generate_series(1, 10000) AS i ORDER BY i;

INSERT INTO nodes (id, parent_id, value)
SELECT c.id, coalesce(p.id, '018cc251-f400-7000-8000-00000000003a'), 'Chain ' || c.i
FROM chain AS c LEFT JOIN chain AS p ON p.i = c.i - 1
ORDER BY c.i;

ANALYZE nodes;
