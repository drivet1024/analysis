WITH RECURSIVE pickup_days AS (
    SELECT CAST('2026-07-27' AS DATE) AS pickup_date
    UNION ALL
    SELECT pickup_date + INTERVAL 1 DAY FROM pickup_days
    WHERE pickup_date < CAST('2026-08-25' AS DATE)
), scheduled_pickups AS (
    SELECT d.pickup_date, p.ROUTE_ID AS route_id, p.CUSTOMER_ID AS customer_id,
           COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client
    FROM pickup_days d
    JOIN customer_schedule_pickup p ON CASE DAYOFWEEK(d.pickup_date)
        WHEN 1 THEN p.SUNDAY WHEN 2 THEN p.MONDAY WHEN 3 THEN p.TUESDAY
        WHEN 4 THEN p.WEDNESDAY WHEN 5 THEN p.THURSDAY WHEN 6 THEN p.FRIDAY
        WHEN 7 THEN p.SATURDAY END = 1
    JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
       AND c.ACTIVE = 1 AND LOWER(c.NAME) NOT LIKE '%nationex%'
    WHERE p.ROUTE_ID IN (50103, 50104, 50125, 50129, 50130, 50144)
    UNION ALL
    SELECT d.pickup_date, c.PU_ROUTE_ID, c.CUSTOMER_ID,
           COALESCE(c.NAME, CONCAT('Client ', c.CUSTOMER_ID))
    FROM pickup_days d
    JOIN customer c ON c.ACTIVE = 1
       AND c.PU_ROUTE_ID IN (50103, 50104, 50125, 50129, 50130, 50144)
       AND LOWER(c.NAME) NOT LIKE '%nationex%'
       AND CASE DAYOFWEEK(d.pickup_date)
           WHEN 1 THEN c.PUSUNDAY WHEN 2 THEN c.PUMONDAY WHEN 3 THEN c.PUTUESDAY
           WHEN 4 THEN c.PUWEDNESDAY WHEN 5 THEN c.PUTHURSDAY WHEN 6 THEN c.PUFRIDAY
           WHEN 7 THEN c.PUSATURDAY END = 1
), daily_counts AS (
    SELECT sp.pickup_date, sp.route_id, sp.customer_id, sp.client,
           COALESCE(SUM(s.PARCEL_NB), 0) AS parcels_created
    FROM scheduled_pickups sp
    LEFT JOIN shipment s ON s.CUSTOMER_ID = sp.customer_id
       AND s.INSERT_DATE >= sp.pickup_date
       AND s.INSERT_DATE < sp.pickup_date + INTERVAL 1 DAY
    GROUP BY sp.pickup_date, sp.route_id, sp.customer_id, sp.client
)
SELECT customer_id, client,
       GROUP_CONCAT(DISTINCT route_id ORDER BY route_id SEPARATOR ' / ') AS route_ids,
       COUNT(*) AS scheduled_pickup_days,
       SUM(parcels_created) AS total_parcels_created,
       ROUND(AVG(parcels_created), 2) AS average_parcels_per_day,
       MAX(parcels_created) AS max_parcels_in_one_day,
       GROUP_CONCAT(CONCAT(DATE_FORMAT(pickup_date, '%Y-%m-%d'), ':', parcels_created) ORDER BY pickup_date SEPARATOR ' | ') AS daily_parcels
FROM daily_counts
GROUP BY customer_id, client
ORDER BY route_ids, client;
