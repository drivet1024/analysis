SELECT 'columns' AS section, c.TABLE_NAME AS object_name, c.ORDINAL_POSITION AS position,
       c.COLUMN_NAME AS attribute, c.COLUMN_TYPE AS definition, c.COLUMN_KEY AS key_type,
       c.IS_NULLABLE AS nullable, c.COLUMN_DEFAULT AS default_value
FROM information_schema.COLUMNS AS c
WHERE c.TABLE_SCHEMA = DATABASE()
  AND c.TABLE_NAME IN ('customer', 'customer_schedule_pickup', 'shipment', 'parcel_history')
UNION ALL
SELECT 'indexes', s.TABLE_NAME, s.SEQ_IN_INDEX, s.INDEX_NAME,
       CONCAT(s.COLUMN_NAME, ' ', s.INDEX_TYPE), s.NON_UNIQUE, NULL, NULL
FROM information_schema.STATISTICS AS s
WHERE s.TABLE_SCHEMA = DATABASE()
  AND s.TABLE_NAME IN ('customer', 'customer_schedule_pickup', 'shipment', 'parcel_history')
ORDER BY section, object_name, position, attribute;
