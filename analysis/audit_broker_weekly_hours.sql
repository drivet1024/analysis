WITH broker_customers AS (
    SELECT c.*
    FROM customer c
    JOIN route r ON r.ROUTE_ID = c.PU_ROUTE_ID
    JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
    WHERE si.SECTOR_TYPE IN (1, 2)
      AND (c.PUSUNDAY = 1 OR c.PUMONDAY = 1 OR c.PUTUESDAY = 1
           OR c.PUWEDNESDAY = 1 OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1
           OR c.PUSATURDAY = 1)
), day_slots AS (
    SELECT CUSTOMER_ID, 'Dim' AS day_name, COALESCE(NULLIF(TRIM(PUTIMESUNDAY), ''), NULLIF(TRIM(PUTIME), '')) AS pickup_time,
           COALESCE(NULLIF(TRIM(CLOSINGTIMESUNDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) AS closing_time FROM broker_customers WHERE PUSUNDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Lun', COALESCE(NULLIF(TRIM(PUTIMEMONDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMEMONDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUMONDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Mar', COALESCE(NULLIF(TRIM(PUTIMETUESDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMETUESDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUTUESDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Mer', COALESCE(NULLIF(TRIM(PUTIMEWEDNESDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMEWEDNESDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUWEDNESDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Jeu', COALESCE(NULLIF(TRIM(PUTIMETHURSDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMETHURSDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUTHURSDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Ven', COALESCE(NULLIF(TRIM(PUTIMEFRIDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMEFRIDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUFRIDAY = 1
    UNION ALL SELECT CUSTOMER_ID, 'Sam', COALESCE(NULLIF(TRIM(PUTIMESATURDAY), ''), NULLIF(TRIM(PUTIME), '')), COALESCE(NULLIF(TRIM(CLOSINGTIMESATURDAY), ''), NULLIF(TRIM(CLOSINGTIME), '')) FROM broker_customers WHERE PUSATURDAY = 1
), per_customer AS (
    SELECT CUSTOMER_ID,
           COUNT(*) AS scheduled_days,
           COUNT(DISTINCT CONCAT(COALESCE(pickup_time, 'N/D'), '|', COALESCE(closing_time, 'N/D'))) AS distinct_hour_pairs,
           MIN(pickup_time) AS one_pickup_time,
           MIN(closing_time) AS one_closing_time
    FROM day_slots
    GROUP BY CUSTOMER_ID
)
SELECT
    COUNT(*) AS customers,
    SUM(distinct_hour_pairs = 1) AS same_pair_all_scheduled_days,
    SUM(distinct_hour_pairs > 1) AS different_pair_by_day,
    SUM(distinct_hour_pairs = 1 AND one_pickup_time IS NOT NULL AND one_closing_time IS NOT NULL) AS compact_complete,
    SUM(distinct_hour_pairs = 1 AND (one_pickup_time IS NULL OR one_closing_time IS NULL)) AS compact_with_missing_value
FROM per_customer;
