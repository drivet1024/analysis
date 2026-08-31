SELECT
    c.TABLE_NAME AS table_name,
    c.ORDINAL_POSITION AS position,
    c.COLUMN_NAME AS column_name,
    c.COLUMN_TYPE AS column_type,
    c.IS_NULLABLE AS is_nullable,
    c.COLUMN_KEY AS column_key,
    c.COLUMN_COMMENT AS column_comment
FROM information_schema.COLUMNS AS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND c.TABLE_NAME IN ('live_route', 'live_route_stats')
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;

SELECT
    s.TABLE_NAME AS table_name,
    s.INDEX_NAME AS index_name,
    s.NON_UNIQUE AS non_unique,
    GROUP_CONCAT(s.COLUMN_NAME ORDER BY s.SEQ_IN_INDEX SEPARATOR ', ') AS indexed_columns
FROM information_schema.STATISTICS AS s
WHERE s.TABLE_SCHEMA = DATABASE()
  AND s.TABLE_NAME IN ('live_route', 'live_route_stats')
GROUP BY s.TABLE_NAME, s.INDEX_NAME, s.NON_UNIQUE
ORDER BY s.TABLE_NAME, s.INDEX_NAME;
