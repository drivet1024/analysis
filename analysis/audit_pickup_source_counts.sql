SELECT 'active_customer_with_pickup_day' AS check_name,
       COUNT(*) AS row_count,
       COUNT(DISTINCT c.CUSTOMER_ID) AS customer_count,
       COUNT(DISTINCT c.PU_ROUTE_ID) AS route_count
FROM customer c
WHERE c.ACTIVE = 1
  AND (c.PUMONDAY = 1 OR c.PUTUESDAY = 1 OR c.PUWEDNESDAY = 1
       OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1 OR c.PUSATURDAY = 1 OR c.PUSUNDAY = 1)
UNION ALL
SELECT 'active_customer_pickup_day_with_route',
       COUNT(*), COUNT(DISTINCT c.CUSTOMER_ID), COUNT(DISTINCT c.PU_ROUTE_ID)
FROM customer c
WHERE c.ACTIVE = 1
  AND c.PU_ROUTE_ID IS NOT NULL
  AND (c.PUMONDAY = 1 OR c.PUTUESDAY = 1 OR c.PUWEDNESDAY = 1
       OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1 OR c.PUSATURDAY = 1 OR c.PUSUNDAY = 1)
UNION ALL
SELECT 'active_customer_pickup_day_broker_route',
       COUNT(*), COUNT(DISTINCT c.CUSTOMER_ID), COUNT(DISTINCT c.PU_ROUTE_ID)
FROM customer c
JOIN route r ON r.ROUTE_ID = c.PU_ROUTE_ID
JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
WHERE c.ACTIVE = 1
  AND si.SECTOR_TYPE IN (1, 2)
  AND (c.PUMONDAY = 1 OR c.PUTUESDAY = 1 OR c.PUWEDNESDAY = 1
       OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1 OR c.PUSATURDAY = 1 OR c.PUSUNDAY = 1)
UNION ALL
SELECT 'customer_pickup_planif',
       COUNT(*), COUNT(DISTINCT CUSTOMER_ID), 0
FROM customer_pickup_planif;
