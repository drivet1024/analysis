SELECT 'all_schedule_clients' AS category, COUNT(DISTINCT p.CUSTOMER_ID) AS client_count
FROM customer_schedule_pickup p
UNION ALL
SELECT 'active_schedule_clients', COUNT(DISTINCT p.CUSTOMER_ID)
FROM customer_schedule_pickup p
JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE c.ACTIVE = 1
UNION ALL
SELECT 'active_route_le_50100_clients', COUNT(DISTINCT p.CUSTOMER_ID)
FROM customer_schedule_pickup p
JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE c.ACTIVE = 1 AND p.ROUTE_ID <= 50100
UNION ALL
SELECT 'active_route_name_filtered_clients', COUNT(DISTINCT p.CUSTOMER_ID)
FROM customer_schedule_pickup p
JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE c.ACTIVE = 1 AND p.ROUTE_ID <= 50100 AND LOWER(c.NAME) NOT LIKE '%nationex%'
UNION ALL
SELECT 'analysis_30_day_clients', COUNT(DISTINCT x.customer_id)
FROM (
    SELECT p.CUSTOMER_ID AS customer_id
    FROM customer_schedule_pickup p
    JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
    WHERE c.ACTIVE = 1 AND p.ROUTE_ID <= 50100 AND LOWER(c.NAME) NOT LIKE '%nationex%'
      AND (p.MONDAY = 1 OR p.TUESDAY = 1 OR p.WEDNESDAY = 1 OR p.THURSDAY = 1 OR p.FRIDAY = 1 OR p.SATURDAY = 1 OR p.SUNDAY = 1)
) x;
