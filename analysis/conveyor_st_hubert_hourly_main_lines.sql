SELECT
    DATE_FORMAT(psh.date_insert, '%Y-%m-%d %H:00:00') AS EVENT_HOUR,
    CAST(psh.line_id AS CHAR) AS LINE_ID,
    COUNT(*) AS SCAN_ROWS,
    SUM(psh.parcel_id IS NOT NULL AND psh.parcel_id <> 0) AS READABLE_SCAN_ROWS,
    COUNT(DISTINCT NULLIF(psh.parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
    SUM(psh.parcel_id IS NULL OR psh.parcel_id = 0) AS UNREADABLE_SCAN_ROWS
FROM parcel_scan_history AS psh
WHERE psh.depot_id = 1
  AND psh.line_id IN (0, 1, 3)
  AND psh.date_insert >= '2026-07-12 17:00:00'
  AND psh.date_insert <  '2026-07-13 08:00:00'
GROUP BY EVENT_HOUR, psh.line_id
ORDER BY EVENT_HOUR, psh.line_id;
