SELECT
    DATE_FORMAT(psh.date_insert, '%Y-%m-%d %H:00:00') AS EVENT_HOUR,
    psh.line_id,
    COUNT(*) AS SCAN_ROWS,
    COUNT(DISTINCT NULLIF(psh.parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
    SUM(psh.parcel_id IS NULL OR psh.parcel_id = 0) AS UNREADABLE_ROWS,
    SUM(psh.chute IS NULL) AS MISSING_CHUTE_ROWS
FROM parcel_scan_history AS psh
WHERE psh.depot_id = 1
  AND psh.date_insert >= '2026-07-12 12:00:00'
  AND psh.date_insert <  '2026-07-13 12:00:00'
GROUP BY EVENT_HOUR, psh.line_id
ORDER BY EVENT_HOUR, psh.line_id;
