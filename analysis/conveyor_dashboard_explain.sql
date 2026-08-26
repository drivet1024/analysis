EXPLAIN
SELECT
    DATE_FORMAT(date_insert, '%Y-%m-%d %H:00:00') AS EVENT_HOUR,
    CASE
        WHEN line_id = 3 THEN 'Sol'
        WHEN line_id IN (0, 1) THEN 'Haut'
    END AS CONVEYOR_GROUP,
    COUNT(*) AS RAW_PASSAGES
FROM parcel_scan_history
WHERE depot_id = 1
  AND line_id IN (0, 1, 3)
  AND date_insert >= '2026-07-12 17:00:00'
  AND date_insert < '2026-07-13 08:00:00'
GROUP BY EVENT_HOUR, CONVEYOR_GROUP;
