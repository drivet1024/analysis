WITH pickup_days AS (
    SELECT CAST('2026-08-03' AS DATE) AS pickup_date, 'Lundi' AS jour, 'MONDAY' AS weekday_name
    UNION ALL SELECT CAST('2026-08-04' AS DATE), 'Mardi', 'TUESDAY'
    UNION ALL SELECT CAST('2026-08-05' AS DATE), 'Mercredi', 'WEDNESDAY'
    UNION ALL SELECT CAST('2026-08-06' AS DATE), 'Jeudi', 'THURSDAY'
    UNION ALL SELECT CAST('2026-08-07' AS DATE), 'Vendredi', 'FRIDAY'
    UNION ALL SELECT CAST('2026-08-08' AS DATE), 'Samedi', 'SATURDAY'
    UNION ALL SELECT CAST('2026-08-09' AS DATE), 'Dimanche', 'SUNDAY'
), scheduled_pickups AS (
    SELECT d.pickup_date, d.jour,
        p.ROUTE_ID AS route_id,
        p.CUSTOMER_ID AS customer_id,
        COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client,
        p.START_TIME AS scheduled_start,
        p.END_TIME AS scheduled_end
    FROM pickup_days d
    JOIN customer_schedule_pickup p
      ON CASE d.weekday_name
           WHEN 'MONDAY' THEN p.MONDAY
           WHEN 'TUESDAY' THEN p.TUESDAY
           WHEN 'WEDNESDAY' THEN p.WEDNESDAY
           WHEN 'THURSDAY' THEN p.THURSDAY
           WHEN 'FRIDAY' THEN p.FRIDAY
           WHEN 'SATURDAY' THEN p.SATURDAY
           WHEN 'SUNDAY' THEN p.SUNDAY
         END = 1
     AND p.ROUTE_ID <= 50100
    JOIN customer c
      ON c.CUSTOMER_ID = p.CUSTOMER_ID
     AND c.ACTIVE = 1
     AND LOWER(c.NAME) NOT LIKE '%nationex%'
), scheduled_clients AS (
  SELECT
    pickup_date,
    jour,
    customer_id,
    client,
    GROUP_CONCAT(DISTINCT route_id ORDER BY route_id SEPARATOR ' / ') AS route_ids,
    GROUP_CONCAT(DISTINCT COALESCE(TIME_FORMAT(scheduled_start, '%H:%i'), 'Non définie') ORDER BY scheduled_start SEPARATOR ' / ') AS scheduled_starts,
    GROUP_CONCAT(DISTINCT TIME_FORMAT(scheduled_end, '%H:%i') ORDER BY scheduled_end SEPARATOR ' / ') AS scheduled_ends
  FROM scheduled_pickups
  GROUP BY pickup_date, jour, customer_id, client
)
SELECT
  sc.pickup_date,
  sc.jour,
  sc.route_ids,
  sc.customer_id,
  sc.client,
  sc.scheduled_starts,
  sc.scheduled_ends,
  COALESCE(SUM(s.PARCEL_NB), 0) AS parcels_created,
    COUNT(DISTINCT s.ID) AS shipments_created,
    COUNT(DISTINCT s.SHIPPING_ID) AS shipping_orders_created
FROM scheduled_clients sc
LEFT JOIN shipment s
  ON s.CUSTOMER_ID = sc.customer_id
 AND s.INSERT_DATE >= sc.pickup_date
 AND s.INSERT_DATE < sc.pickup_date + INTERVAL 1 DAY
GROUP BY sc.pickup_date, sc.jour, sc.route_ids, sc.customer_id, sc.client,
     sc.scheduled_starts, sc.scheduled_ends
HAVING COALESCE(SUM(s.PARCEL_NB), 0) <= 100
ORDER BY sc.pickup_date, sc.scheduled_starts, sc.client;
