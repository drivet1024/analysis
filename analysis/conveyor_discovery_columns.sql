SELECT
    c.TABLE_NAME,
    c.ORDINAL_POSITION,
    c.COLUMN_NAME,
    c.COLUMN_TYPE,
    c.COLUMN_KEY,
    c.COLUMN_COMMENT
FROM information_schema.COLUMNS AS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND (
      c.TABLE_NAME LIKE '%conveyor%'
      OR c.TABLE_NAME IN (
          'depot',
          'location',
          'parcel_history',
          'parcel_scan_history',
          'parcel'
      )
  )
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
