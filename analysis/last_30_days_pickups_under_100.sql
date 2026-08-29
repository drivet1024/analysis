WITH RECURSIVE pickup_days AS (
    SELECT CAST('2026-07-27' AS DATE) AS pickup_date
    UNION ALL
    SELECT pickup_date + INTERVAL 1 DAY
    FROM pickup_days
    WHERE pickup_date < CAST('2026-08-25' AS DATE)
), scheduled_pickups AS (
    SELECT
        d.pickup_date,
        p.ROUTE_ID AS route_id,
        p.CUSTOMER_ID AS customer_id,
        COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client,
        p.START_TIME AS scheduled_start,
        p.END_TIME AS scheduled_end
    FROM pickup_days d
    JOIN customer_schedule_pickup p
      ON CASE DAYOFWEEK(d.pickup_date)
           WHEN 1 THEN p.SUNDAY
           WHEN 2 THEN p.MONDAY
           WHEN 3 THEN p.TUESDAY
           WHEN 4 THEN p.WEDNESDAY
           WHEN 5 THEN p.THURSDAY
           WHEN 6 THEN p.FRIDAY
           WHEN 7 THEN p.SATURDAY
         END = 1
    AND p.ROUTE_ID BETWEEN 50000 AND 50099
    AND NOT EXISTS (
        SELECT 1
        FROM customer_schedule_pickup excluded
        WHERE excluded.CUSTOMER_ID = p.CUSTOMER_ID
          AND excluded.ROUTE_ID IN (50007, 50012, 50014, 50027, 55609)
    )
    JOIN customer c
      ON c.CUSTOMER_ID = p.CUSTOMER_ID
     AND c.ACTIVE = 1
     AND LOWER(c.NAME) NOT LIKE '%nationex%'
), scheduled_clients AS (
    SELECT
        pickup_date,
        customer_id,
        client,
        GROUP_CONCAT(DISTINCT route_id ORDER BY route_id SEPARATOR ' / ') AS route_ids,
        GROUP_CONCAT(DISTINCT COALESCE(TIME_FORMAT(scheduled_start, '%H:%i'), 'Non définie') ORDER BY scheduled_start SEPARATOR ' / ') AS scheduled_starts,
        GROUP_CONCAT(DISTINCT TIME_FORMAT(scheduled_end, '%H:%i') ORDER BY scheduled_end SEPARATOR ' / ') AS scheduled_ends
    FROM scheduled_pickups
    GROUP BY pickup_date, customer_id, client
), scheduled_client_ids AS (
    SELECT DISTINCT customer_id FROM scheduled_clients
), parcel_latest_ranked AS (
        SELECT p.PARCEL_ID, p.SHIPPING_ID, p.EXP_DATE, p.WEIGHT, p.LENGTH, p.WIDTH, p.HEIGHT,
                     ROW_NUMBER() OVER (PARTITION BY p.PARCEL_ID ORDER BY p.UPDATE_DATE DESC, p.INSERT_DATE DESC) AS row_rank
        FROM parcel p
        WHERE p.INSERT_DATE >= CAST('2026-07-26' AS DATETIME)
            AND p.INSERT_DATE < CAST('2026-08-26' AS DATETIME)
), parcel_measurements AS (
        SELECT s.CUSTOMER_ID AS customer_id,
                     AVG(CASE WHEN pl.LENGTH > 0 AND pl.WIDTH > 0 AND pl.HEIGHT > 0
                                        THEN pl.LENGTH * pl.WIDTH * pl.HEIGHT END) AS average_cubic_volume,
                     COUNT(CASE WHEN pl.LENGTH > 0 AND pl.WIDTH > 0 AND pl.HEIGHT > 0 THEN 1 END) AS volume_measured_parcels,
                     AVG(CASE WHEN pl.WEIGHT > 0 THEN pl.WEIGHT END) AS average_weight,
                     COUNT(CASE WHEN pl.WEIGHT > 0 THEN 1 END) AS weight_measured_parcels
        FROM shipment s
        JOIN scheduled_client_ids sc ON sc.customer_id = s.CUSTOMER_ID
        JOIN parcel_latest_ranked pl
            ON pl.SHIPPING_ID = s.SHIPPING_ID
         AND (pl.EXP_DATE = s.EXP_DATE OR (pl.EXP_DATE IS NULL AND s.EXP_DATE IS NULL))
         AND pl.row_rank = 1
        WHERE s.INSERT_DATE >= CAST('2026-07-27' AS DATETIME)
            AND s.INSERT_DATE < CAST('2026-08-26' AS DATETIME)
        GROUP BY s.CUSTOMER_ID
), daily_counts AS (
    SELECT
        sc.pickup_date,
        sc.customer_id,
        sc.client,
        sc.route_ids,
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
    GROUP BY sc.pickup_date, sc.customer_id, sc.client, sc.route_ids,
             sc.scheduled_starts, sc.scheduled_ends
)
SELECT
    daily_counts.customer_id,
    daily_counts.client,
    GROUP_CONCAT(DISTINCT route_ids ORDER BY route_ids SEPARATOR ' / ') AS route_ids,
    COUNT(*) AS scheduled_pickup_days,
    SUM(parcels_created <= 100) AS days_at_or_below_100,
    ROUND(100.0 * SUM(parcels_created <= 100) / COUNT(*), 2) AS percent_days_at_or_below_100,
    SUM(parcels_created) AS total_parcels_created,
    ROUND(AVG(parcels_created), 2) AS average_parcels_per_pickup_day,
    MAX(parcels_created) AS max_parcels_in_one_day,
    SUM(shipments_created) AS total_shipments_created,
    ROUND(MAX(pm.average_cubic_volume), 2) AS average_cubic_volume,
    MAX(pm.volume_measured_parcels) AS volume_measured_parcels,
    ROUND(MAX(pm.average_weight), 2) AS average_weight,
    MAX(pm.weight_measured_parcels) AS weight_measured_parcels,
    MIN(pickup_date) AS first_pickup_day,
    MAX(pickup_date) AS last_pickup_day
FROM daily_counts
LEFT JOIN parcel_measurements pm ON pm.customer_id = daily_counts.customer_id
GROUP BY daily_counts.customer_id, daily_counts.client
ORDER BY percent_days_at_or_below_100 DESC, max_parcels_in_one_day, client;
