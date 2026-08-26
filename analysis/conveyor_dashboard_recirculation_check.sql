WITH parcel_counts AS (
    SELECT
        CASE
            WHEN line_id = 3 THEN 'Sol'
            WHEN line_id IN (0, 1) THEN 'Haut'
        END AS CONVEYOR_GROUP,
        parcel_id,
        COUNT(*) AS SCAN_COUNT
    FROM parcel_scan_history
    WHERE depot_id = 1
      AND line_id IN (0, 1, 3)
      AND date_insert >= '2026-07-12 17:00:00'
      AND date_insert < '2026-07-13 08:00:00'
      AND parcel_id IS NOT NULL
      AND parcel_id <> 0
    GROUP BY CONVEYOR_GROUP, parcel_id
)
SELECT
    CONVEYOR_GROUP,
    COUNT(*) AS UNIQUE_READABLE_PARCELS,
    SUM(SCAN_COUNT > 1) AS RECIRCULATED_PARCELS,
    SUM(SCAN_COUNT - 1) AS EXTRA_SCAN_ROWS,
    MAX(SCAN_COUNT) AS MAX_SCANS_FOR_ONE_PARCEL
FROM parcel_counts
GROUP BY CONVEYOR_GROUP
ORDER BY FIELD(CONVEYOR_GROUP, 'Haut', 'Sol');
