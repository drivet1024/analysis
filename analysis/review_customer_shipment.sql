SELECT 'table_rows' AS section, TABLE_NAME AS item, TABLE_ROWS AS value, NULL AS detail
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('customer', 'customer_scedule_pickup', 'customer_schedule_pickup', 'shipment', 'parcel_history')
UNION ALL
SELECT 'table_exists', t.TABLE_NAME, 1, t.TABLE_COMMENT
FROM information_schema.TABLES AS t
WHERE t.TABLE_SCHEMA = DATABASE()
  AND t.TABLE_NAME IN ('customer', 'customer_scedule_pickup', 'customer_schedule_pickup', 'shipment', 'parcel_history')
ORDER BY section, item;
