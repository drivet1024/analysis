SELECT TABLE_NAME,ORDINAL_POSITION,COLUMN_NAME,DATA_TYPE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA=DATABASE()
  AND TABLE_NAME IN ('parcel_history_source_id','nat_user','route','depot','exceptions','sub_exceptions')
ORDER BY TABLE_NAME,ORDINAL_POSITION;
