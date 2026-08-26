SELECT
    DATE_FORMAT(date_insert, '%Y-%m-%d %H:00:00') AS EVENT_HOUR,
    CASE
        WHEN line_id = 3 THEN 'Sol'
        WHEN line_id IN (0, 1) THEN 'Haut'
    END AS CONVEYOR_GROUP,
    COUNT(*) AS RAW_PASSAGES,
    COUNT(DISTINCT NULLIF(parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
    SUM(parcel_id IS NULL OR parcel_id = 0) AS UNREADABLE_SCAN_ROWS,
    SUM(
        l IS NULL OR l <= 0
        OR h IS NULL OR h <= 0
        OR w IS NULL OR w <= 0
    ) AS MISSING_DIMENSION_SCAN_ROWS,
    SUM(weight IS NULL OR weight <= 0) AS MISSING_WEIGHT_SCAN_ROWS
FROM parcel_scan_history
WHERE depot_id = 1
  AND line_id IN (0, 1, 3)
  AND date_insert >= '2026-07-12 17:00:00'
  AND date_insert < '2026-07-13 08:00:00'
GROUP BY EVENT_HOUR, CONVEYOR_GROUP
ORDER BY EVENT_HOUR, FIELD(CONVEYOR_GROUP, 'Haut', 'Sol');
