SELECT
    c.TABLE_NAME AS table_name,
    c.ORDINAL_POSITION AS ordinal_position,
    c.COLUMN_NAME AS column_name,
    c.COLUMN_TYPE AS column_type,
    c.IS_NULLABLE AS is_nullable,
    c.COLUMN_KEY AS column_key,
    c.COLUMN_COMMENT AS column_comment
FROM information_schema.COLUMNS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND (
      c.TABLE_NAME IN ('customer_schedule_pickup', 'customer_pickup_planif')
      OR (
          c.TABLE_NAME = 'customer'
          AND (
              c.COLUMN_NAME IN ('CUSTOMER_ID', 'NAME', 'ACTIVE', 'PU_ROUTE_ID', 'CUST_PU_NATIONEX_ROUTE')
              OR c.COLUMN_NAME REGEXP '^PU(MONDAY|TUESDAY|WEDNESDAY|THURSDAY|FRIDAY|SATURDAY|SUNDAY)$'
              OR c.COLUMN_NAME REGEXP '^PUTIME'
          )
      )
  )
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
