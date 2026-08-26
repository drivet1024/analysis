SELECT
    t.TABLE_NAME,
    t.TABLE_TYPE,
    t.TABLE_ROWS,
    t.TABLE_COMMENT
FROM information_schema.TABLES AS t
WHERE t.TABLE_SCHEMA = DATABASE()
  AND (
      t.TABLE_NAME LIKE '%conveyor%'
      OR t.TABLE_NAME LIKE '%sorter%'
      OR t.TABLE_NAME LIKE '%depot%'
      OR t.TABLE_NAME LIKE '%location%'
  )
ORDER BY t.TABLE_NAME;
