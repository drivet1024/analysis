WITH latest AS (
    SELECT MAX(ph.DATE_LIV) AS max_event_time
    FROM parcel_history ph
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.DATE_INSERT >= NOW() - INTERVAL 2 DAY
), scoped AS (
    SELECT
        ph.PARCEL_ID,
        COALESCE(NULLIF(ph.CUSTOMER_ID, 0), NULLIF(p.CUSTOMER_ID, 0)) AS customer_id,
        ph.DATE_LIV AS event_time,
        CASE WHEN ph.SOURCE_ID = 3 THEN 'Sol' ELSE 'Haut' END AS conveyor_line
    FROM parcel_history ph
    CROSS JOIN latest l
    LEFT JOIN parcel p ON p.PARCEL_ID = ph.PARCEL_ID
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.DATE_INSERT >= l.max_event_time - INTERVAL 2 HOUR
      AND ph.DATE_INSERT <  l.max_event_time + INTERVAL 1 DAY
      AND ph.DATE_LIV >= l.max_event_time - INTERVAL 60 MINUTE
      AND ph.DATE_LIV <= l.max_event_time
), parcel_rollup AS (
    SELECT
        s.PARCEL_ID,
        MAX(s.customer_id) AS customer_id,
        MIN(s.event_time) AS first_event,
        MAX(s.event_time) AS last_event,
        SUBSTRING_INDEX(GROUP_CONCAT(s.conveyor_line ORDER BY s.event_time DESC), ',', 1) AS conveyor_line
    FROM scoped s
    GROUP BY s.PARCEL_ID
)
SELECT
    NOW() AS database_now,
    l.max_event_time,
    pr.customer_id,
    COALESCE(c.NAME, CONCAT('Client ', pr.customer_id)) AS customer_name,
    c.PU_ROUTE_ID AS customer_pickup_route,
    pr.conveyor_line,
    COUNT(*) AS parcels_60m,
    SUM(pr.last_event >= l.max_event_time - INTERVAL 30 MINUTE) AS parcels_30m,
    SUM(pr.last_event >= l.max_event_time - INTERVAL 20 MINUTE) AS parcels_20m,
    SUM(pr.last_event >= l.max_event_time - INTERVAL 10 MINUTE) AS parcels_10m,
    MIN(pr.first_event) AS first_seen_60m,
    MAX(pr.last_event) AS last_seen
FROM parcel_rollup pr
CROSS JOIN latest l
LEFT JOIN customer c ON c.CUSTOMER_ID = pr.customer_id
GROUP BY database_now, l.max_event_time, pr.customer_id, c.NAME, c.PU_ROUTE_ID, pr.conveyor_line
ORDER BY parcels_10m DESC, parcels_20m DESC, parcels_60m DESC
LIMIT 100;
