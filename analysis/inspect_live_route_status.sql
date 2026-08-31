SELECT
    c.ORDINAL_POSITION AS position,
    c.COLUMN_NAME AS column_name,
    c.COLUMN_TYPE AS column_type,
    c.IS_NULLABLE AS is_nullable,
    c.COLUMN_KEY AS column_key,
    c.COLUMN_COMMENT AS column_comment
FROM information_schema.COLUMNS AS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND c.TABLE_NAME = 'live_route_status'
ORDER BY c.ORDINAL_POSITION;
