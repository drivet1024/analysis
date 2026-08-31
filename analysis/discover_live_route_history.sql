SELECT
    t.TABLE_NAME AS table_name,
    t.TABLE_TYPE AS table_type,
    t.TABLE_ROWS AS estimated_rows,
    t.TABLE_COMMENT AS table_comment
FROM information_schema.TABLES AS t
WHERE t.TABLE_SCHEMA = DATABASE()
  AND t.TABLE_NAME LIKE 'live%'
ORDER BY t.TABLE_NAME;

SELECT
    c.TABLE_NAME AS table_name,
    c.COLUMN_NAME AS column_name,
    c.COLUMN_TYPE AS column_type,
    c.COLUMN_KEY AS column_key,
    c.COLUMN_COMMENT AS column_comment
FROM information_schema.COLUMNS AS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND c.TABLE_NAME LIKE 'live%'
  AND (
      c.COLUMN_NAME REGEXP 'customer|pickup|arrival|time|date|stop|status|route'
  )
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
