WITH RECURSIVE
route_map_raw AS (
    SELECT
        p.CUSTOMER_ID AS customer_id,
        COUNT(DISTINCT p.ROUTE_ID) AS route_count,
        MIN(p.ROUTE_ID) AS route_id
    FROM customer_schedule_pickup p
    WHERE p.ROUTE_ID BETWEEN 50000 AND 50099
      AND CASE DAYOFWEEK(CURDATE())
            WHEN 1 THEN p.SUNDAY
            WHEN 2 THEN p.MONDAY
            WHEN 3 THEN p.TUESDAY
            WHEN 4 THEN p.WEDNESDAY
            WHEN 5 THEN p.THURSDAY
            WHEN 6 THEN p.FRIDAY
            WHEN 7 THEN p.SATURDAY
          END = 1
    GROUP BY p.CUSTOMER_ID
),
route_map AS (
    SELECT customer_id, route_id
    FROM route_map_raw
    WHERE route_count = 1
),
scanned_parcels AS (
    SELECT
        ph.PARCEL_ID AS parcel_id,
        MAX(NULLIF(ph.CUSTOMER_ID, 0)) AS customer_id,
        MIN(ph.DATE_LIV) AS first_seen,
        MAX(ph.DATE_LIV) AS last_seen
    FROM parcel_history ph
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID = 1)
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.DATE_INSERT >= CURDATE() + INTERVAL 15 HOUR
      AND ph.DATE_INSERT < CURDATE() + INTERVAL 1 DAY
      AND ph.DATE_LIV >= CURDATE() + INTERVAL 16 HOUR
      AND ph.DATE_LIV < CURDATE() + INTERVAL 1 DAY
    GROUP BY ph.PARCEL_ID
),
passed_by_route AS (
    SELECT
        rm.route_id,
        COUNT(*) AS parcels_passed,
        SUM(sp.last_seen >= NOW() - INTERVAL 5 MINUTE) AS parcels_last_5m,
        MIN(sp.first_seen) AS first_seen,
        MAX(sp.last_seen) AS last_seen
    FROM scanned_parcels sp
    JOIN route_map rm ON rm.customer_id = sp.customer_id
    GROUP BY rm.route_id
),
today_created AS (
    SELECT
        rm.route_id,
        COALESCE(SUM(s.PARCEL_NB), 0) AS parcels_created_today
    FROM route_map rm
    JOIN shipment s ON s.CUSTOMER_ID = rm.customer_id
      AND s.INSERT_DATE >= CURDATE()
      AND s.INSERT_DATE < CURDATE() + INTERVAL 1 DAY
    GROUP BY rm.route_id
),
history_created AS (
    SELECT
        rm.route_id,
        ROUND(COALESCE(SUM(s.PARCEL_NB), 0) / 4.0) AS historical_average
    FROM route_map rm
    JOIN shipment s ON s.CUSTOMER_ID = rm.customer_id
      AND s.INSERT_DATE >= CURDATE() - INTERVAL 28 DAY
      AND s.INSERT_DATE < CURDATE()
      AND DAYOFWEEK(s.INSERT_DATE) = DAYOFWEEK(CURDATE())
    GROUP BY rm.route_id
),
route_metrics AS (
    SELECT
        pbr.route_id,
        pbr.parcels_passed,
        pbr.parcels_last_5m,
        COALESCE(tc.parcels_created_today, 0) AS parcels_created_today,
        COALESCE(hc.historical_average, 0) AS historical_average,
        GREATEST(
            pbr.parcels_passed,
            COALESCE(tc.parcels_created_today, 0),
            COALESCE(hc.historical_average, 0)
        ) AS estimated_total,
        pbr.first_seen,
        pbr.last_seen
    FROM passed_by_route pbr
    LEFT JOIN today_created tc ON tc.route_id = pbr.route_id
    LEFT JOIN history_created hc ON hc.route_id = pbr.route_id
)
SELECT
    NOW() AS database_now,
    (SELECT MAX(last_seen) FROM scanned_parcels) AS latest_scan,
    (SELECT COUNT(*) FROM scanned_parcels) AS total_high_parcels,
    (SELECT COUNT(*) FROM scanned_parcels sp JOIN route_map rm ON rm.customer_id = sp.customer_id) AS mapped_high_parcels,
    (SELECT COUNT(*) FROM scanned_parcels sp LEFT JOIN route_map_raw rmr ON rmr.customer_id = sp.customer_id WHERE rmr.customer_id IS NULL) AS unmapped_high_parcels,
    (SELECT COUNT(*) FROM scanned_parcels sp JOIN route_map_raw rmr ON rmr.customer_id = sp.customer_id WHERE rmr.route_count > 1) AS ambiguous_high_parcels,
    rm.route_id,
    rm.parcels_passed,
    rm.parcels_last_5m,
    rm.parcels_created_today,
    rm.historical_average,
    rm.estimated_total,
    GREATEST(rm.estimated_total - rm.parcels_passed, 0) AS estimated_remaining,
    ROUND(100.0 * rm.parcels_passed / NULLIF(rm.estimated_total, 0), 1) AS estimated_progress_pct,
    rm.first_seen,
    rm.last_seen
FROM route_metrics rm
ORDER BY
    (rm.last_seen >= NOW() - INTERVAL 5 MINUTE) DESC,
    rm.parcels_last_5m DESC,
    rm.last_seen DESC,
    rm.route_id;
