SELECT json_build_object(
  'nodes', (SELECT count(*) FROM nodes),
  'tableSize', pg_size_pretty(pg_total_relation_size('nodes')),
  'bulk', (SELECT id FROM nodes WHERE value = 'Bulk'),
  'group500', (SELECT id FROM nodes WHERE value = 'Group 500'),
  'group500LastCursor', (
    SELECT id FROM nodes
    WHERE parent_id = (SELECT id FROM nodes WHERE value = 'Group 500')
    ORDER BY id OFFSET 979 LIMIT 1),
  'chain10', (SELECT id FROM nodes WHERE value = 'Chain 10'),
  'chain1000', (SELECT id FROM nodes WHERE value = 'Chain 1000'),
  'chain9999', (SELECT id FROM nodes WHERE value = 'Chain 9999'),
  'chain10000', (SELECT id FROM nodes WHERE value = 'Chain 10000'),
  'leaf', (SELECT id FROM nodes WHERE value = 'Item 7.1'),
  'items', (SELECT json_agg(id) FROM (
    SELECT id FROM nodes WHERE value LIKE 'Item 8.%' ORDER BY id LIMIT 17) AS s),
  'archive', '018cc251-f400-7000-8000-00000000003a');
