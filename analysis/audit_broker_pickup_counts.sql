SELECT 'all_schedule_rows' AS check_name,
       COUNT(*) AS row_count,
       COUNT(DISTINCT CUSTOMER_ID) AS customer_count,
       COUNT(DISTINCT ROUTE_ID) AS route_count
FROM customer_schedule_pickup
UNION ALL
SELECT 'schedule_rows_with_any_day',
       COUNT(*), COUNT(DISTINCT CUSTOMER_ID), COUNT(DISTINCT ROUTE_ID)
FROM customer_schedule_pickup
WHERE SUNDAY = 1 OR MONDAY = 1 OR TUESDAY = 1 OR WEDNESDAY = 1
   OR THURSDAY = 1 OR FRIDAY = 1 OR SATURDAY = 1
UNION ALL
SELECT 'schedule_rows_direct_broker_route',
       COUNT(*), COUNT(DISTINCT p.CUSTOMER_ID), COUNT(DISTINCT p.ROUTE_ID)
FROM customer_schedule_pickup p
JOIN route r ON r.ROUTE_ID = p.ROUTE_ID
JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
WHERE si.SECTOR_TYPE IN (1, 2)
UNION ALL
SELECT 'schedule_rows_direct_nationex_route',
       COUNT(*), COUNT(DISTINCT p.CUSTOMER_ID), COUNT(DISTINCT p.ROUTE_ID)
FROM customer_schedule_pickup p
JOIN route r ON r.ROUTE_ID = p.ROUTE_ID
JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
WHERE si.SECTOR_TYPE = 0
UNION ALL
SELECT 'schedule_rows_unmatched_route_or_sector',
       COUNT(*), COUNT(DISTINCT p.CUSTOMER_ID), COUNT(DISTINCT p.ROUTE_ID)
FROM customer_schedule_pickup p
LEFT JOIN route r ON r.ROUTE_ID = p.ROUTE_ID
LEFT JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
WHERE r.ROUTE_ID IS NULL OR si.SECTOR_ID IS NULL;
