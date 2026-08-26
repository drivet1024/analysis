WITH monday_dates AS (
    SELECT CAST('2026-08-03' AS DATE) AS pickup_date
    UNION ALL SELECT CAST('2026-08-10' AS DATE)
    UNION ALL SELECT CAST('2026-08-17' AS DATE)
    UNION ALL SELECT CAST('2026-08-24' AS DATE)
), monday_clients AS (
    SELECT DISTINCT
        d.pickup_date,
        p.ROUTE_ID AS route_id,
        p.CUSTOMER_ID AS customer_id,
        COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client,
        p.START_TIME AS scheduled_start,
        p.END_TIME AS scheduled_end
    FROM monday_dates d
    JOIN customer_schedule_pickup p ON p.MONDAY = 1
    LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
)
SELECT
    mc.pickup_date,
    mc.route_id,
    mc.customer_id,
    mc.client,
    mc.scheduled_start,
    mc.scheduled_end,
    COUNT(DISTINCT s.ID) AS shipments_created,
    COUNT(DISTINCT s.SHIPPING_ID) AS shipping_orders_created,
    MIN(s.INSERT_DATE) AS first_created_at,
    MAX(s.INSERT_DATE) AS last_created_at
FROM monday_clients mc
JOIN shipment s
  ON s.CUSTOMER_ID = mc.customer_id
 AND s.INSERT_DATE >= mc.pickup_date
 AND s.INSERT_DATE < mc.pickup_date + INTERVAL 1 DAY
GROUP BY mc.pickup_date, mc.route_id, mc.customer_id, mc.client,
         mc.scheduled_start, mc.scheduled_end
ORDER BY mc.pickup_date, mc.scheduled_start IS NULL, mc.scheduled_start, mc.client;
