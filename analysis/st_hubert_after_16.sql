WITH scoped AS (
    SELECT
        ph.PARCEL_ID,
        COALESCE(NULLIF(ph.CUSTOMER_ID, 0), NULLIF(p.CUSTOMER_ID, 0)) AS customer_id,
        ph.DATE_LIV AS event_time,
        CASE WHEN ph.SOURCE_ID = 3 THEN 'Sol' ELSE 'Haut' END AS conveyor_line
    FROM parcel_history ph
    LEFT JOIN parcel p ON p.PARCEL_ID = ph.PARCEL_ID
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.DATE_INSERT >= CURDATE() + INTERVAL 15 HOUR
      AND ph.DATE_INSERT <  CURDATE() + INTERVAL 1 DAY
      AND ph.DATE_LIV >= CURDATE() + INTERVAL 16 HOUR
      AND ph.DATE_LIV <  CURDATE() + INTERVAL 1 DAY
), parcel_rollup AS (
    SELECT
        PARCEL_ID,
        MAX(customer_id) AS customer_id,
        MIN(event_time) AS first_event,
        MAX(event_time) AS last_event,
        SUBSTRING_INDEX(GROUP_CONCAT(conveyor_line ORDER BY event_time DESC), ',', 1) AS conveyor_line
    FROM scoped
    GROUP BY PARCEL_ID
)
SELECT
    NOW() AS database_now,
    pr.customer_id,
    COALESCE(c.NAME, CONCAT('Client ', pr.customer_id)) AS customer_name,
    pr.conveyor_line,
    COUNT(*) AS parcels_after_16,
    MIN(pr.first_event) AS first_seen,
    MAX(pr.last_event) AS last_seen
FROM parcel_rollup pr
LEFT JOIN customer c ON c.CUSTOMER_ID = pr.customer_id
GROUP BY database_now, pr.customer_id, c.NAME, pr.conveyor_line
ORDER BY parcels_after_16 DESC, last_seen DESC;
